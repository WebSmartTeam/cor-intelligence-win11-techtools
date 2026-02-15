using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Network;

/// <summary>
/// Monitors active TCP/UDP connections with process ownership — equivalent to TCPView.
/// Uses netstat -ano for reliable PID-to-connection mapping, then resolves
/// process names via System.Diagnostics.Process.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class ConnectionMonitorService : IConnectionMonitorService
{
    public async Task<List<ConnectionEntry>> GetActiveConnectionsAsync()
    {
        // Run netstat -ano to get all connections with PIDs
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        var entries = new List<ConnectionEntry>();
        var processNameCache = new Dictionary<int, string>();
        var lines = output.Split('\n', StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            // Match TCP lines (have state column)
            var tcpMatch = TcpLineRegex().Match(line);
            if (tcpMatch.Success)
            {
                var (localAddr, localPort) = ParseEndpoint(tcpMatch.Groups[1].Value);
                var (remoteAddr, remotePort) = ParseEndpoint(tcpMatch.Groups[2].Value);
                var state = tcpMatch.Groups[3].Value;
                var pid = int.Parse(tcpMatch.Groups[4].Value);

                entries.Add(new ConnectionEntry
                {
                    Protocol = "TCP",
                    LocalAddress = localAddr,
                    LocalPort = localPort,
                    RemoteAddress = remoteAddr,
                    RemotePort = remotePort,
                    State = state,
                    ProcessId = pid,
                    ProcessName = ResolveProcessName(pid, processNameCache)
                });
                continue;
            }

            // Match UDP lines (no state column)
            var udpMatch = UdpLineRegex().Match(line);
            if (udpMatch.Success)
            {
                var (localAddr, localPort) = ParseEndpoint(udpMatch.Groups[1].Value);
                var (remoteAddr, remotePort) = ParseEndpoint(udpMatch.Groups[2].Value);
                var pid = int.Parse(udpMatch.Groups[3].Value);

                entries.Add(new ConnectionEntry
                {
                    Protocol = "UDP",
                    LocalAddress = localAddr,
                    LocalPort = localPort,
                    RemoteAddress = remoteAddr,
                    RemotePort = remotePort,
                    State = "*",
                    ProcessId = pid,
                    ProcessName = ResolveProcessName(pid, processNameCache)
                });
            }
        }

        return entries
            .OrderBy(e => e.Protocol)
            .ThenBy(e => e.State)
            .ThenBy(e => e.LocalPort)
            .ToList();
    }

    public Task KillProcessAsync(int processId)
    {
        return Task.Run(() =>
        {
            if (processId <= 0)
                throw new ArgumentException("Process ID must be a positive integer.", nameof(processId));

            // Prevent killing critical system processes
            if (processId == 0 || processId == 4)
                throw new InvalidOperationException("Cannot terminate system-critical processes (PID 0 or 4).");

            try
            {
                using var proc = Process.GetProcessById(processId);
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(5000);
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException($"Process with PID {processId} is no longer running.");
            }
        });
    }

    private static string ResolveProcessName(int pid, Dictionary<int, string> cache)
    {
        if (cache.TryGetValue(pid, out var cached))
            return cached;

        string name;
        try
        {
            using var proc = Process.GetProcessById(pid);
            name = proc.ProcessName;
        }
        catch (SystemException)
        {
            // Process may have exited between netstat and lookup
            name = pid == 0 ? "System Idle Process" : $"PID {pid}";
        }

        cache[pid] = name;
        return name;
    }

    private static (string Address, int Port) ParseEndpoint(string endpoint)
    {
        // Handle IPv6 [::]:port and IPv4 0.0.0.0:port
        var lastColon = endpoint.LastIndexOf(':');
        if (lastColon < 0) return (endpoint, 0);

        var addr = endpoint[..lastColon];
        var portStr = endpoint[(lastColon + 1)..];

        if (int.TryParse(portStr, out var port))
            return (addr, port);

        return (addr, 0);
    }

    // TCP line: "  TCP    192.168.1.5:52341      52.14.133.20:443       ESTABLISHED     12345"
    [GeneratedRegex(@"^\s*TCP\s+(\S+)\s+(\S+)\s+(\w+)\s+(\d+)")]
    private static partial Regex TcpLineRegex();

    // UDP line: "  UDP    0.0.0.0:5353           *:*                                    5678"
    [GeneratedRegex(@"^\s*UDP\s+(\S+)\s+(\S+)\s+(\d+)")]
    private static partial Regex UdpLineRegex();
}

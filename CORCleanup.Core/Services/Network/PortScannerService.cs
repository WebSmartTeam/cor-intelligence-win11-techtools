using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Network;

[SupportedOSPlatform("windows")]
public sealed partial class PortScannerService : IPortScannerService
{
    // Well-known port names
    private static readonly Dictionary<int, string> WellKnownPorts = new()
    {
        [20] = "FTP Data",
        [21] = "FTP",
        [22] = "SSH",
        [23] = "Telnet",
        [25] = "SMTP",
        [53] = "DNS",
        [67] = "DHCP",
        [68] = "DHCP",
        [80] = "HTTP",
        [110] = "POP3",
        [123] = "NTP",
        [135] = "RPC",
        [137] = "NetBIOS",
        [138] = "NetBIOS",
        [139] = "NetBIOS",
        [143] = "IMAP",
        [161] = "SNMP",
        [389] = "LDAP",
        [443] = "HTTPS",
        [445] = "SMB",
        [465] = "SMTPS",
        [514] = "Syslog",
        [587] = "SMTP (Submit)",
        [636] = "LDAPS",
        [993] = "IMAPS",
        [995] = "POP3S",
        [1433] = "MSSQL",
        [1521] = "Oracle",
        [3306] = "MySQL",
        [3389] = "RDP",
        [5432] = "PostgreSQL",
        [5900] = "VNC",
        [5985] = "WinRM",
        [5986] = "WinRM (SSL)",
        [8080] = "HTTP Alt",
        [8443] = "HTTPS Alt",
        [27017] = "MongoDB",
    };

    public async IAsyncEnumerable<PortScanResult> ScanPortsAsync(
        string host,
        IEnumerable<int> ports,
        int timeoutMs = 2000,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var port in ports)
        {
            ct.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            bool isOpen = false;

            try
            {
                using var client = new TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);

                await client.ConnectAsync(host, port, cts.Token);
                sw.Stop();
                isOpen = true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout — port is closed/filtered
                sw.Stop();
            }
            catch (SocketException)
            {
                // Connection refused — port is closed
                sw.Stop();
            }

            WellKnownPorts.TryGetValue(port, out var serviceName);

            yield return new PortScanResult
            {
                Host = host,
                Port = port,
                Protocol = "TCP",
                IsOpen = isOpen,
                ServiceName = serviceName,
                ResponseTimeMs = sw.ElapsedMilliseconds
            };
        }
    }

    public async Task<List<LocalPortEntry>> GetLocalPortsAsync(CancellationToken ct = default)
    {
        // Use netstat -ano to get all connections with PIDs
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
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var entries = new List<LocalPortEntry>();
        var lines = output.Split('\n', StringSplitOptions.TrimEntries);

        // Build PID → process name cache
        var processNames = new Dictionary<int, string>();

        foreach (var line in lines)
        {
            var match = NetstatLineRegex().Match(line);
            if (!match.Success) continue;

            var protocol = match.Groups[1].Value;
            var localEndpoint = match.Groups[2].Value;
            var remoteEndpoint = match.Groups[3].Value;
            var state = match.Groups[4].Value;
            var pidStr = match.Groups[5].Value;

            if (!int.TryParse(pidStr, out var pid)) continue;

            // Parse local endpoint
            var (localAddr, localPort) = ParseEndpoint(localEndpoint);
            var (remoteAddr, remotePort) = ParseEndpoint(remoteEndpoint);

            if (localPort == 0) continue;

            // Resolve process name (cache to avoid repeated lookups)
            if (!processNames.TryGetValue(pid, out var processName))
            {
                try
                {
                    using var proc = System.Diagnostics.Process.GetProcessById(pid);
                    processName = proc.ProcessName;
                }
                catch (SystemException)
                {
                    // Process exited between netstat and lookup — use PID as fallback
                    processName = $"PID {pid}";
                }
                processNames[pid] = processName;
            }

            entries.Add(new LocalPortEntry
            {
                Protocol = protocol,
                LocalAddress = localAddr,
                LocalPort = localPort,
                RemoteAddress = remoteAddr == "0.0.0.0" || remoteAddr == "*" ? null : remoteAddr,
                RemotePort = remotePort > 0 ? remotePort : null,
                State = state.Length > 0 ? state : "LISTENING",
                ProcessId = pid,
                ProcessName = processName
            });
        }

        return entries
            .OrderBy(e => e.Protocol)
            .ThenBy(e => e.LocalPort)
            .ToList();
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

    // Match netstat -ano output lines
    // "  TCP    0.0.0.0:135            0.0.0.0:0              LISTENING       1234"
    // "  UDP    0.0.0.0:5353           *:*                                    5678"
    [GeneratedRegex(@"^\s*(TCP|UDP)\s+(\S+)\s+(\S+)\s*(\w*)\s+(\d+)")]
    private static partial Regex NetstatLineRegex();

    /// <summary>
    /// Common port presets for quick scanning.
    /// </summary>
    public static class Presets
    {
        public static int[] WebServer => [80, 443, 8080, 8443];
        public static int[] RemoteAccess => [22, 3389, 5900, 5985, 5986];
        public static int[] Email => [25, 110, 143, 465, 587, 993, 995];
        public static int[] Database => [1433, 1521, 3306, 5432, 27017];
        public static int[] CommonAll => [21, 22, 23, 25, 53, 80, 110, 135, 139, 143, 443, 445, 993, 995, 1433, 3306, 3389, 5432, 5900, 8080];
    }
}

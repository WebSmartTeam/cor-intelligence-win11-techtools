using System.Diagnostics;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Tools;

[SupportedOSPlatform("windows")]
public sealed class ProcessExplorerService : IProcessExplorerService
{
    // Well-known system process names (always run as SYSTEM, not killable)
    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit",
        "services", "lsass", "svchost", "dwm", "fontdrvhost",
        "Memory Compression", "System Idle Process"
    };

    public async Task<List<ProcessEntry>> GetProcessesAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            // Snapshot 1: capture CPU times
            var snapshot1 = new Dictionary<int, (TimeSpan Cpu, string Name, Process Proc)>();
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    snapshot1[proc.Id] = (proc.TotalProcessorTime, proc.ProcessName, proc);
                }
                catch
                {
                    // Access denied for some system processes — skip
                    proc.Dispose();
                }
            }

            ct.ThrowIfCancellationRequested();

            // Wait for sampling interval
            Thread.Sleep(500);

            ct.ThrowIfCancellationRequested();

            int cpuCount = Environment.ProcessorCount;
            var entries = new List<ProcessEntry>(snapshot1.Count);

            foreach (var kvp in snapshot1)
            {
                var proc = kvp.Value.Proc;
                try
                {
                    // Snapshot 2: measure CPU delta
                    proc.Refresh();
                    var cpuDelta = proc.TotalProcessorTime - kvp.Value.Cpu;
                    double cpuPercent = cpuDelta.TotalMilliseconds / (500.0 * cpuCount) * 100.0;
                    if (cpuPercent < 0) cpuPercent = 0;
                    if (cpuPercent > 100) cpuPercent = 100;

                    string? filePath = null;
                    string? description = null;
                    string? userName = null;
                    DateTime startTime = DateTime.MinValue;
                    bool isSystem = SystemProcesses.Contains(proc.ProcessName);

                    try { filePath = proc.MainModule?.FileName; } catch { }
                    try { description = proc.MainModule?.FileVersionInfo.FileDescription; } catch { }
                    try { startTime = proc.StartTime; } catch { }

                    entries.Add(new ProcessEntry
                    {
                        Pid = proc.Id,
                        Name = proc.ProcessName,
                        Description = string.IsNullOrWhiteSpace(description) ? null : description,
                        FilePath = filePath,
                        CpuPercent = Math.Round(cpuPercent, 1),
                        WorkingSetBytes = proc.WorkingSet64,
                        PrivateBytes = proc.PrivateMemorySize64,
                        ThreadCount = proc.Threads.Count,
                        HandleCount = proc.HandleCount,
                        StartTime = startTime,
                        UserName = userName,
                        IsSystem = isSystem
                    });
                }
                catch
                {
                    // Process may have exited between snapshots
                }
                finally
                {
                    proc.Dispose();
                }
            }

            return entries
                .OrderByDescending(e => e.CpuPercent)
                .ThenByDescending(e => e.WorkingSetBytes)
                .ToList();
        }, ct);
    }

    public Task<bool> KillProcessAsync(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(3000);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public void OpenFileLocation(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        Process.Start("explorer.exe", $"/select, \"{filePath}\"");
    }
}

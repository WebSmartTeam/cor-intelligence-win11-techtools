using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Tools;

[SupportedOSPlatform("windows")]
public sealed class MemoryExplorerService : IMemoryExplorerService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public Task<MemoryInfo> GetMemoryInfoAsync()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };

        if (!GlobalMemoryStatusEx(ref status))
            throw new InvalidOperationException("Failed to query system memory status.");

        return Task.FromResult(new MemoryInfo
        {
            TotalPhysicalBytes = (long)status.ullTotalPhys,
            AvailablePhysicalBytes = (long)status.ullAvailPhys,
            TotalPageFileBytes = (long)status.ullTotalPageFile,
            AvailablePageFileBytes = (long)status.ullAvailPageFile,
            TotalVirtualBytes = (long)status.ullTotalVirtual,
            AvailableVirtualBytes = (long)status.ullAvailVirtual,
            MemoryLoadPercent = (int)status.dwMemoryLoad
        });
    }

    public async Task<List<MemoryConsumer>> GetTopConsumersAsync(int top = 30, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            long totalPhysical = 0;
            try
            {
                var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref status))
                    totalPhysical = (long)status.ullTotalPhys;
            }
            catch { }

            var consumers = new List<MemoryConsumer>();

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    long ws = proc.WorkingSet64;
                    long priv = proc.PrivateMemorySize64;
                    double pct = totalPhysical > 0
                        ? (double)ws / totalPhysical * 100.0
                        : 0;

                    consumers.Add(new MemoryConsumer
                    {
                        Pid = proc.Id,
                        Name = proc.ProcessName,
                        WorkingSetBytes = ws,
                        PrivateBytes = priv,
                        MemoryPercent = Math.Round(pct, 1)
                    });
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    // Access denied — skip
                }
                finally
                {
                    proc.Dispose();
                }
            }

            return consumers
                .OrderByDescending(c => c.WorkingSetBytes)
                .Take(top)
                .ToList();
        }, ct);
    }
}

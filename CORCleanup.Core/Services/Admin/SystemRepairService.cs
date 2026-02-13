using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Admin;

[SupportedOSPlatform("windows")]
public sealed class SystemRepairService : ISystemRepairService
{
    public Task<SystemRepairResult> RunSfcScanAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        return RunCommandAsync(
            "System File Checker",
            "sfc",
            "/scannow",
            progress,
            ct);
    }

    public Task<SystemRepairResult> RunDismRestoreHealthAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        return RunCommandAsync(
            "DISM Restore Health",
            "DISM.exe",
            "/Online /Cleanup-Image /RestoreHealth",
            progress,
            ct);
    }

    public async Task<SystemRepairResult> ResetNetworkStackAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var commands = new (string Name, string Exe, string Args)[]
        {
            ("Winsock Reset", "netsh", "winsock reset"),
            ("IP Stack Reset", "netsh", "int ip reset"),
            ("DNS Flush", "ipconfig", "/flushdns"),
            ("DNS Re-register", "ipconfig", "/registerdns"),
            ("TCP Reset", "netsh", "int tcp reset"),
        };

        var output = new StringBuilder();
        var sw = Stopwatch.StartNew();
        bool allSuccess = true;

        foreach (var (name, exe, args) in commands)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Running: {name}...");

            var result = await RunCommandAsync(name, exe, args, null, ct);
            output.AppendLine($"--- {name} (exit code {result.ExitCode}) ---");
            output.AppendLine(result.Output);

            if (!result.Success)
            {
                allSuccess = false;
                output.AppendLine($"[ERROR] {result.ErrorOutput}");
            }
        }

        sw.Stop();

        return new SystemRepairResult
        {
            OperationName = "Network Stack Reset",
            Command = "netsh winsock reset + int ip reset + ipconfig /flushdns + /registerdns + int tcp reset",
            Success = allSuccess,
            ExitCode = allSuccess ? 0 : 1,
            Output = output.ToString(),
            Duration = sw.Elapsed
        };
    }

    public Task<SystemRepairResult> FlushDnsAsync(CancellationToken ct = default)
    {
        return RunCommandAsync(
            "DNS Cache Flush",
            "ipconfig",
            "/flushdns",
            null,
            ct);
    }

    public async Task<SystemRepairResult> ResetWindowsUpdateAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var commands = new (string Name, string Exe, string Args)[]
        {
            ("Stop BITS Service", "net", "stop bits"),
            ("Stop Windows Update", "net", "stop wuauserv"),
            ("Stop Cryptographic Services", "net", "stop cryptSvc"),
            ("Stop MSI Server", "net", "stop msiserver"),
            ("Start BITS Service", "net", "start bits"),
            ("Start Windows Update", "net", "start wuauserv"),
            ("Start Cryptographic Services", "net", "start cryptSvc"),
            ("Start MSI Server", "net", "start msiserver"),
        };

        var output = new StringBuilder();
        var sw = Stopwatch.StartNew();
        bool allSuccess = true;

        foreach (var (name, exe, args) in commands)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Running: {name}...");

            var result = await RunCommandAsync(name, exe, args, null, ct);
            output.AppendLine($"--- {name} (exit code {result.ExitCode}) ---");
            output.AppendLine(result.Output);

            if (!result.Success)
            {
                allSuccess = false;
                output.AppendLine($"[ERROR] {result.ErrorOutput}");
            }
        }

        sw.Stop();

        return new SystemRepairResult
        {
            OperationName = "Windows Update Reset",
            Command = "net stop/start bits + wuauserv + cryptSvc + msiserver",
            Success = allSuccess,
            ExitCode = allSuccess ? 0 : 1,
            Output = output.ToString(),
            Duration = sw.Elapsed
        };
    }

    private static async Task<SystemRepairResult> RunCommandAsync(
        string operationName,
        string fileName,
        string arguments,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
                progress?.Report(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderr.AppendLine(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);
            sw.Stop();

            return new SystemRepairResult
            {
                OperationName = operationName,
                Command = $"{fileName} {arguments}",
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Output = stdout.ToString().TrimEnd(),
                ErrorOutput = stderr.ToString().TrimEnd(),
                Duration = sw.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch { /* best effort */ }
            }

            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();

            return new SystemRepairResult
            {
                OperationName = operationName,
                Command = $"{fileName} {arguments}",
                Success = false,
                ExitCode = -1,
                ErrorOutput = ex.Message,
                Duration = sw.Elapsed
            };
        }
    }
}

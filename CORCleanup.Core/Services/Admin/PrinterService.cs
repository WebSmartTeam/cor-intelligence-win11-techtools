using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Core.Security;

namespace CORCleanup.Core.Services.Admin;

[SupportedOSPlatform("windows")]
public sealed class PrinterService : IPrinterService
{
    public Task<List<PrinterInfo>> GetPrintersAsync()
    {
        return Task.Run(() =>
        {
            var printers = new List<PrinterInfo>();

            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DriverName, PortName, Default, Network, PrinterStatus, JobCountSinceLastReset FROM Win32_Printer");

            foreach (ManagementObject printer in searcher.Get())
            {
                using (printer)
                {
                    printers.Add(new PrinterInfo
                    {
                        Name = (printer["Name"] as string) ?? "Unknown",
                        DriverName = printer["DriverName"] as string,
                        PortName = printer["PortName"] as string,
                        IsDefault = (bool)(printer["Default"] ?? false),
                        IsNetwork = (bool)(printer["Network"] ?? false),
                        Status = MapPrinterStatus(printer["PrinterStatus"]),
                        JobCount = Convert.ToInt32(printer["JobCountSinceLastReset"] ?? 0)
                    });
                }
            }

            return printers.OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name).ToList();
        });
    }

    public async Task<bool> ClearSpoolerAsync()
    {
        // Stop spooler → clear queue → start spooler
        var commands = new[]
        {
            ("net", "stop spooler"),
            ("cmd", "/c del /Q /F /S \"%systemroot%\\System32\\spool\\PRINTERS\\*\""),
            ("net", "start spooler")
        };

        foreach (var (exe, args) in commands)
        {
            var exitCode = await RunProcessAsync(exe, args);
            // net stop may return 2 if already stopped — allow it
            if (exitCode != 0 && exitCode != 2)
                return false;
        }

        return true;
    }

    public async Task<bool> RemovePrinterAsync(string printerName)
    {
        // Use PowerShell Remove-Printer cmdlet (Win10/11 built-in)
        var exitCode = await RunProcessAsync(
            "powershell",
            $"-NoProfile -Command \"Remove-Printer -Name '{EscapePowerShell(printerName)}'\"");

        return exitCode == 0;
    }

    public async Task<bool> SetDefaultPrinterAsync(string printerName)
    {
        var exitCode = await RunProcessAsync(
            "powershell",
            $"-NoProfile -Command \"Set-DefaultPrinter -Name '{EscapePowerShell(printerName)}'\"");

        // Fallback via WMI if PowerShell cmdlet not available
        if (exitCode != 0)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        $"SELECT * FROM Win32_Printer WHERE Name = '{EscapeWmi(printerName)}'");

                    foreach (ManagementObject printer in searcher.Get())
                    {
                        using (printer)
                        {
                            printer.InvokeMethod("SetDefaultPrinter", null);
                            return true;
                        }
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            });
        }

        return true;
    }

    public async Task<bool> PrintTestPageAsync(string printerName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Printer WHERE Name = '{EscapeWmi(printerName)}'");

                foreach (ManagementObject printer in searcher.Get())
                {
                    using (printer)
                    {
                        printer.InvokeMethod("PrintTestPage", null);
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        });
    }

    private static string MapPrinterStatus(object? status)
    {
        // Win32_Printer PrinterStatus values
        return Convert.ToInt32(status ?? 0) switch
        {
            1 => "Other",
            2 => "Unknown",
            3 => "Idle",
            4 => "Printing",
            5 => "Warming Up",
            6 => "Stopped Printing",
            7 => "Offline",
            _ => "Ready"
        };
    }

    private static async Task<int> RunProcessAsync(string fileName, string arguments, int timeoutSeconds = 60)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null) return -1;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return -1;
        }
    }

    private static string EscapePowerShell(string value)
        => InputSanitiser.EscapeForPowerShell(value);

    private static string EscapeWmi(string value)
        => InputSanitiser.EscapeWql(value);
}

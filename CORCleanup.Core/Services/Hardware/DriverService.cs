using System.Management;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Hardware;

/// <summary>
/// Enumerates all signed device drivers via Win32_PnPSignedDriver.
///
/// Filters out entries with null/empty device names (phantom or virtual devices)
/// and sorts by DriverDate ascending so the oldest (most outdated) drivers appear
/// first — useful for identifying drivers that need updating.
///
/// Note: Uses Win32_PnPSignedDriver, NOT Win32_SystemDriver (which only lists
/// kernel-mode drivers). PnPSignedDriver covers all hardware device drivers
/// including display, audio, network, USB, etc.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DriverService : IDriverService
{
    public Task<List<DriverInfo>> GetAllDriversAsync() => Task.Run(() =>
    {
        var drivers = new List<DriverInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion, Manufacturer, DeviceClass, DriverDate " +
                "FROM Win32_PnPSignedDriver");

            foreach (var obj in searcher.Get())
            {
                var deviceName = obj["DeviceName"]?.ToString();

                // Skip entries with no device name — these are phantom/virtual devices
                if (string.IsNullOrWhiteSpace(deviceName))
                    continue;

                var driverVersion = obj["DriverVersion"]?.ToString() ?? "Unknown";
                var manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "Unknown";
                var deviceClass = obj["DeviceClass"]?.ToString() ?? "Unknown";

                // Parse driver date — WMI returns CIM_DATETIME format (yyyyMMddHHmmss.ffffff+zzz)
                var driverDate = DateTime.MinValue;
                var dateStr = obj["DriverDate"]?.ToString();
                if (!string.IsNullOrEmpty(dateStr))
                {
                    try
                    {
                        driverDate = ManagementDateTimeConverter.ToDateTime(dateStr);

                        // Sanity check: discard impossibly old or future dates
                        if (driverDate.Year < 2000 || driverDate > DateTime.Now)
                            driverDate = DateTime.MinValue;
                    }
                    catch
                    {
                        // Date conversion failure — leave as MinValue
                    }
                }

                drivers.Add(new DriverInfo
                {
                    DeviceName = deviceName.Trim(),
                    DriverVersion = driverVersion,
                    Manufacturer = manufacturer,
                    DeviceClass = deviceClass,
                    DriverDate = driverDate
                });
            }
        }
        catch (ManagementException)
        {
            // WMI query failure — return empty list
        }

        // Sort oldest first — helps identify outdated drivers at a glance
        return drivers
            .OrderBy(d => d.DriverDate)
            .ToList();
    });
}

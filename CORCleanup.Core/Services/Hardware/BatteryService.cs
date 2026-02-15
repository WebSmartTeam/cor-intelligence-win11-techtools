using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Xml.Linq;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Hardware;

/// <summary>
/// Retrieves battery health information using a two-tier strategy:
///
/// 1. Primary: Run <c>powercfg /batteryreport /xml</c> which produces an XML file
///    containing DesignCapacity, FullChargeCapacity, and CycleCount — the most
///    accurate source available on Windows 11.
///
/// 2. Fallback: Query root\WMI classes (BatteryStaticData, BatteryFullChargedCapacity,
///    BatteryCycleCount) directly via WMI — works on hardware where powercfg fails
///    or is restricted by group policy.
///
/// Win32_Battery is used in both paths for charge percentage and charging status.
/// Returns HasBattery=false on desktops or when no battery is detected.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BatteryService : IBatteryService
{
    public Task<BatteryInfo> GetBatteryInfoAsync() => Task.Run(() =>
    {
        // Quick check: is there actually a battery?
        var winBat = QueryFirst("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
        if (winBat is null)
        {
            return new BatteryInfo
            {
                DesignCapacityMwh = 0,
                FullChargeCapacityMwh = 0,
                CycleCount = 0,
                Chemistry = "N/A",
                Manufacturer = "N/A",
                ChargePercent = 0,
                IsCharging = false,
                HasBattery = false
            };
        }

        var chargePercent = GetInt(winBat, "EstimatedChargeRemaining");
        var batteryStatus = GetUShort(winBat, "BatteryStatus");
        // BatteryStatus: 2 = AC Power, 6-9 = Charging variants
        var isCharging = batteryStatus is 2 or 6 or 7 or 8 or 9;

        // Try powercfg first (most accurate), then WMI fallback
        var report = TryGetFromPowercfg();
        report ??= TryGetFromWmi();

        return new BatteryInfo
        {
            DesignCapacityMwh = report?.DesignCapacityMwh ?? 0,
            FullChargeCapacityMwh = report?.FullChargeCapacityMwh ?? 0,
            CycleCount = report?.CycleCount ?? 0,
            Chemistry = report?.Chemistry ?? "Unknown",
            Manufacturer = report?.Manufacturer ?? "Unknown",
            ChargePercent = chargePercent,
            IsCharging = isCharging,
            HasBattery = true
        };
    });

    // --- Primary: powercfg /batteryreport /xml ---

    private static BatteryReport? TryGetFromPowercfg()
    {
        string? xmlPath = null;

        try
        {
            var tempDir = Path.GetTempPath();
            xmlPath = Path.Combine(tempDir, "cor-cleanup-battery-report.xml");

            // Delete stale report if it exists
            if (File.Exists(xmlPath))
                File.Delete(xmlPath);

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"/batteryreport /xml /output \"{xmlPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            process.Start();
            process.WaitForExit(10_000); // 10 second timeout

            if (process.ExitCode != 0 || !File.Exists(xmlPath))
                return null;

            var doc = XDocument.Load(xmlPath);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            // Navigate: BatteryReport > Batteries > Battery
            var battery = doc.Root?
                .Element(ns + "Batteries")?
                .Element(ns + "Battery");

            if (battery is null)
                return null;

            var designCap = ParseLong(battery.Element(ns + "DesignCapacity")?.Value);
            var fullChargeCap = ParseLong(battery.Element(ns + "FullChargeCapacity")?.Value);
            var cycleCount = ParseInt(battery.Element(ns + "CycleCount")?.Value);
            var chemistry = battery.Element(ns + "Chemistry")?.Value ?? "Unknown";
            var manufacturer = battery.Element(ns + "ManufactureName")?.Value ?? "Unknown";

            // powercfg reports capacity in mWh already
            return new BatteryReport
            {
                DesignCapacityMwh = designCap,
                FullChargeCapacityMwh = fullChargeCap,
                CycleCount = cycleCount,
                Chemistry = NormaliseChemistry(chemistry),
                Manufacturer = manufacturer.Trim()
            };
        }
        catch
        {
            // powercfg may fail on virtual machines, restricted environments, etc.
            return null;
        }
        finally
        {
            // Clean up temporary file
            try
            {
                if (xmlPath is not null && File.Exists(xmlPath))
                    File.Delete(xmlPath);
            }
            catch
            {
                // Non-critical cleanup failure
            }
        }
    }

    // --- Fallback: root\WMI battery classes ---

    private static BatteryReport? TryGetFromWmi()
    {
        try
        {
            long designCap = 0;
            long fullChargeCap = 0;
            int cycleCount = 0;
            string chemistry = "Unknown";
            string manufacturer = "Unknown";

            // BatteryStaticData — design capacity and chemistry
            using (var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT DesignedCapacity, Chemistry, ManufactureName FROM BatteryStaticData"))
            {
                foreach (var obj in searcher.Get())
                {
                    designCap = GetLong(obj, "DesignedCapacity");
                    var chemCode = GetInt(obj, "Chemistry");
                    chemistry = DecodeChemistry(chemCode);
                    manufacturer = GetString(obj, "ManufactureName", "Unknown").Trim();
                }
            }

            // BatteryFullChargedCapacity — current maximum
            using (var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
            {
                foreach (var obj in searcher.Get())
                {
                    fullChargeCap = GetLong(obj, "FullChargedCapacity");
                }
            }

            // BatteryCycleCount — charge cycles completed
            using (var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT CycleCount FROM BatteryCycleCount"))
            {
                foreach (var obj in searcher.Get())
                {
                    cycleCount = GetInt(obj, "CycleCount");
                }
            }

            // Only return if we actually got design capacity (confirms real data)
            if (designCap <= 0)
                return null;

            return new BatteryReport
            {
                DesignCapacityMwh = designCap,
                FullChargeCapacityMwh = fullChargeCap,
                CycleCount = cycleCount,
                Chemistry = chemistry,
                Manufacturer = manufacturer
            };
        }
        catch (ManagementException)
        {
            // root\WMI battery classes not available on all hardware
            return null;
        }
    }

    // --- Chemistry decoding ---

    private static string DecodeChemistry(int code) => code switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => "Lead Acid",
        4 => "Nickel Cadmium",
        5 => "Nickel Metal Hydride",
        6 => "Lithium-ion",
        7 => "Zinc Air",
        8 => "Lithium Polymer",
        _ => $"Type {code}"
    };

    /// <summary>
    /// Normalise chemistry strings from powercfg XML to human-readable form.
    /// powercfg reports e.g. "LiP" or "Li-I" or "LiOn".
    /// </summary>
    private static string NormaliseChemistry(string raw) => raw.Trim().ToUpperInvariant() switch
    {
        "LIP" or "LIPO" => "Lithium Polymer",
        "LI-I" or "LION" or "LI-ION" or "LIION" => "Lithium-ion",
        "NIMH" => "Nickel Metal Hydride",
        "NICD" => "Nickel Cadmium",
        "PBAC" => "Lead Acid",
        _ => raw.Trim()
    };

    // --- Parse helpers ---

    private static long ParseLong(string? value)
    {
        if (value is null) return 0;
        return long.TryParse(value, out var result) ? result : 0;
    }

    private static int ParseInt(string? value)
    {
        if (value is null) return 0;
        return int.TryParse(value, out var result) ? result : 0;
    }

    // --- WMI helpers ---

    private static ManagementBaseObject? QueryFirst(string query)
    {
        using var searcher = new ManagementObjectSearcher(query);
        foreach (var obj in searcher.Get())
            return obj;
        return null;
    }

    private static string GetString(ManagementBaseObject obj, string property, string fallback) =>
        obj[property]?.ToString() ?? fallback;

    private static int GetInt(ManagementBaseObject obj, string property)
    {
        var val = obj[property];
        return val is not null ? Convert.ToInt32(val) : 0;
    }

    private static ushort GetUShort(ManagementBaseObject obj, string property)
    {
        var val = obj[property];
        return val is not null ? Convert.ToUInt16(val) : (ushort)0;
    }

    private static long GetLong(ManagementBaseObject obj, string property)
    {
        var val = obj[property];
        return val is not null ? Convert.ToInt64(val) : 0;
    }

    // --- Internal report record ---

    private sealed record BatteryReport
    {
        public required long DesignCapacityMwh { get; init; }
        public required long FullChargeCapacityMwh { get; init; }
        public required int CycleCount { get; init; }
        public required string Chemistry { get; init; }
        public required string Manufacturer { get; init; }
    }
}

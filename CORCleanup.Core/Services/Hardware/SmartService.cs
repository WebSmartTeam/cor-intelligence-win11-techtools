using System.Management;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Hardware;

/// <summary>
/// Dedicated S.M.A.R.T. disk health monitoring service.
/// Queries Win32_DiskDrive for disk metadata, then MSStorageDriver_FailurePredictData
/// (root\WMI) for raw S.M.A.R.T. attribute bytes, and MSStorageDriver_FailurePredictStatus
/// for the firmware's own failure prediction flag.
///
/// Each S.M.A.R.T. attribute occupies 12 bytes:
///   [0]    = Attribute ID
///   [1-2]  = Flags (vendor-specific)
///   [3]    = Current normalised value (1-253, higher = healthier)
///   [4]    = Worst recorded normalised value
///   [5-10] = 6 raw-value bytes (little-endian)
///   [11]   = Threshold (only available via FailurePredictThresholds — optional)
///
/// Falls back to MSFT_StorageReliabilityCounter (root\Microsoft\Windows\Storage)
/// for temperature, power-on hours, and wear percentage when raw S.M.A.R.T. is unavailable.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SmartService : ISmartService
{
    /// <summary>
    /// Well-known S.M.A.R.T. attribute IDs mapped to human-readable names.
    /// Covers the critical subset used for health assessment.
    /// </summary>
    private static readonly Dictionary<int, string> KnownAttributes = new()
    {
        [1]   = "Read Error Rate",
        [3]   = "Spin-Up Time",
        [4]   = "Start/Stop Count",
        [5]   = "Reallocated Sectors Count",
        [7]   = "Seek Error Rate",
        [9]   = "Power-On Hours",
        [10]  = "Spin Retry Count",
        [12]  = "Power Cycle Count",
        [187] = "Reported Uncorrectable Errors",
        [188] = "Command Timeout",
        [190] = "Airflow Temperature",
        [194] = "Temperature",
        [196] = "Reallocation Event Count",
        [197] = "Current Pending Sector Count",
        [198] = "Uncorrectable Sector Count",
        [199] = "UltraDMA CRC Error Count",
        [200] = "Multi-Zone Error Rate",
        [231] = "SSD Life Left",
        [232] = "Endurance Remaining",
        [233] = "Media Wearout Indicator",
        [241] = "Total LBAs Written",
        [242] = "Total LBAs Read"
    };

    /// <summary>
    /// Attribute IDs that indicate imminent drive failure when their normalised
    /// value drops below threshold. A single failure here marks the drive as Bad.
    /// </summary>
    private static readonly HashSet<int> CriticalAttributes = new()
    {
        5,   // Reallocated Sectors Count
        10,  // Spin Retry Count
        187, // Reported Uncorrectable Errors
        196, // Reallocation Event Count
        197, // Current Pending Sector Count
        198  // Uncorrectable Sector Count
    };

    public Task<List<DiskHealthInfo>> GetAllDiskHealthAsync() => Task.Run(() =>
    {
        var disks = new List<DiskHealthInfo>();

        // Step 1: Enumerate physical disks for metadata
        var diskMetadata = GetDiskMetadata();
        if (diskMetadata.Count == 0)
            return disks;

        // Step 2: Attempt raw S.M.A.R.T. attribute parsing
        var smartDataByIndex = GetSmartRawData();
        var failurePrediction = GetFailurePredictionStatus();

        // Step 3: Build DiskHealthInfo for each drive
        for (var i = 0; i < diskMetadata.Count; i++)
        {
            var meta = diskMetadata[i];
            var attributes = smartDataByIndex.GetValueOrDefault(i, new List<SmartAttribute>());
            var predictedFailure = failurePrediction.GetValueOrDefault(i, false);

            var health = AssessHealth(attributes, predictedFailure);

            var diskInfo = new DiskHealthInfo
            {
                Model = meta.Model,
                SerialNumber = meta.SerialNumber,
                FirmwareRevision = meta.FirmwareRevision,
                SizeBytes = meta.SizeBytes,
                InterfaceType = meta.InterfaceType,
                MediaType = meta.MediaType,
                OverallHealth = health,
                TemperatureCelsius = GetAttributeRaw(attributes, 194)
                    ?? GetAttributeRaw(attributes, 190),
                PowerOnHours = GetAttributeRawLong(attributes, 9),
                ReallocatedSectors = GetAttributeRawInt(attributes, 5),
                PendingSectors = GetAttributeRawInt(attributes, 197),
                WearLevellingPercent = GetAttributeRawInt(attributes, 231)
                    ?? GetAttributeRawInt(attributes, 232)
                    ?? GetAttributeRawInt(attributes, 233),
                SmartAttributes = attributes
            };

            disks.Add(diskInfo);
        }

        // Step 4: Fill in gaps from MSFT_StorageReliabilityCounter (Win11 Storage namespace)
        TryEnrichFromStorageNamespace(disks);

        return disks;
    });

    // --- Disk metadata from Win32_DiskDrive ---

    private static List<DiskMeta> GetDiskMetadata()
    {
        var result = new List<DiskMeta>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Model, SerialNumber, FirmwareRevision, Size, InterfaceType, MediaType " +
                "FROM Win32_DiskDrive");

            foreach (var obj in searcher.Get())
            {
                var rawMedia = GetString(obj, "MediaType", "Unknown");
                var model = GetString(obj, "Model", "Unknown").Trim();

                // WMI MediaType is unreliable for SSDs — use heuristics
                var mediaType = rawMedia.Contains("SSD", StringComparison.OrdinalIgnoreCase)
                    || rawMedia.Contains("Solid", StringComparison.OrdinalIgnoreCase)
                    || model.Contains("SSD", StringComparison.OrdinalIgnoreCase)
                    || model.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
                    ? "SSD"
                    : rawMedia.Contains("Fixed", StringComparison.OrdinalIgnoreCase)
                        ? "HDD"
                        : rawMedia;

                result.Add(new DiskMeta
                {
                    Model = model,
                    SerialNumber = GetString(obj, "SerialNumber", "Unknown").Trim(),
                    FirmwareRevision = GetString(obj, "FirmwareRevision", "Unknown").Trim(),
                    SizeBytes = GetLong(obj, "Size"),
                    InterfaceType = GetString(obj, "InterfaceType", "Unknown"),
                    MediaType = mediaType
                });
            }
        }
        catch (ManagementException)
        {
            // WMI unavailable — return empty
        }

        return result;
    }

    // --- Raw S.M.A.R.T. bytes from root\WMI ---

    private static Dictionary<int, List<SmartAttribute>> GetSmartRawData()
    {
        var result = new Dictionary<int, List<SmartAttribute>>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT VendorSpecific, InstanceName FROM MSStorageDriver_FailurePredictData");

            var driveIndex = 0;
            foreach (var obj in searcher.Get())
            {
                var vendorSpecific = obj["VendorSpecific"] as byte[];
                if (vendorSpecific is null || vendorSpecific.Length < 2)
                {
                    driveIndex++;
                    continue;
                }

                var attributes = ParseSmartBytes(vendorSpecific);
                result[driveIndex] = attributes;
                driveIndex++;
            }
        }
        catch (ManagementException)
        {
            // root\WMI S.M.A.R.T. classes not available — NVMe drives often lack this
        }

        return result;
    }

    /// <summary>
    /// Parse the VendorSpecific byte array from MSStorageDriver_FailurePredictData.
    /// The array starts with a 2-byte header, then each attribute is 12 bytes.
    /// </summary>
    private static List<SmartAttribute> ParseSmartBytes(byte[] data)
    {
        var attributes = new List<SmartAttribute>();

        // Skip 2-byte header; each attribute block is 12 bytes
        const int headerSize = 2;
        const int attributeSize = 12;

        for (var offset = headerSize;
             offset + attributeSize <= data.Length;
             offset += attributeSize)
        {
            var id = data[offset];
            if (id == 0) continue; // Empty slot

            var current = data[offset + 3];
            var worst = data[offset + 4];

            // 6-byte raw value at offset+5, little-endian
            long rawValue = 0;
            for (var b = 5; b >= 0; b--)
            {
                rawValue = (rawValue << 8) | data[offset + 5 + b];
            }

            // Temperature raw values are sometimes packed: low byte is Celsius
            var effectiveRaw = rawValue;
            if (id is 194 or 190 && rawValue > 200)
            {
                effectiveRaw = rawValue & 0xFF;
            }

            var name = KnownAttributes.GetValueOrDefault(id, $"Attribute {id}");

            // Determine per-attribute health status
            // Without vendor thresholds we use normalised value heuristics:
            // >= 100 = good, 50-99 = caution for critical attrs, < 50 = bad for critical attrs
            var status = DiskHealthStatus.Good;
            if (CriticalAttributes.Contains(id))
            {
                if (current < 50)
                    status = DiskHealthStatus.Bad;
                else if (current < 100 && effectiveRaw > 0)
                    status = DiskHealthStatus.Caution;
            }

            attributes.Add(new SmartAttribute
            {
                Id = id,
                Name = name,
                CurrentValue = current,
                WorstValue = worst,
                Threshold = 0, // Thresholds require FailurePredictThresholds — often unavailable
                RawValue = effectiveRaw,
                Status = status
            });
        }

        return attributes;
    }

    // --- Failure prediction flag ---

    private static Dictionary<int, bool> GetFailurePredictionStatus()
    {
        var result = new Dictionary<int, bool>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus");

            var driveIndex = 0;
            foreach (var obj in searcher.Get())
            {
                var predictFailure = obj["PredictFailure"];
                if (predictFailure is bool pf)
                    result[driveIndex] = pf;

                driveIndex++;
            }
        }
        catch (ManagementException)
        {
            // Not available on all drives
        }

        return result;
    }

    // --- Health assessment ---

    private static DiskHealthStatus AssessHealth(
        List<SmartAttribute> attributes,
        bool predictedFailure)
    {
        if (predictedFailure)
            return DiskHealthStatus.Bad;

        if (attributes.Count == 0)
            return DiskHealthStatus.Unknown;

        var badCount = 0;
        var cautionCount = 0;

        foreach (var attr in attributes)
        {
            if (!CriticalAttributes.Contains(attr.Id))
                continue;

            if (attr.Status == DiskHealthStatus.Bad)
                badCount++;
            else if (attr.Status == DiskHealthStatus.Caution)
                cautionCount++;
        }

        if (badCount > 0)
            return DiskHealthStatus.Bad;
        if (cautionCount > 0)
            return DiskHealthStatus.Caution;

        return DiskHealthStatus.Good;
    }

    // --- Enrichment from MSFT_StorageReliabilityCounter ---

    private static void TryEnrichFromStorageNamespace(List<DiskHealthInfo> disks)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT Temperature, PowerOnHours, Wear, ReadErrorsTotal FROM MSFT_StorageReliabilityCounter");

            var idx = 0;
            foreach (var obj in searcher.Get())
            {
                if (idx >= disks.Count) break;
                var disk = disks[idx];
                var updated = disk;

                // Only fill in values that are still null from raw S.M.A.R.T. parsing
                if (!disk.TemperatureCelsius.HasValue)
                {
                    var temp = obj["Temperature"];
                    if (temp is not null)
                        updated = updated with { TemperatureCelsius = Convert.ToInt32(temp) };
                }

                if (!disk.PowerOnHours.HasValue)
                {
                    var hours = obj["PowerOnHours"];
                    if (hours is not null)
                        updated = updated with { PowerOnHours = Convert.ToInt64(hours) };
                }

                if (!disk.WearLevellingPercent.HasValue)
                {
                    var wear = obj["Wear"];
                    if (wear is not null)
                        updated = updated with { WearLevellingPercent = Convert.ToInt32(wear) };
                }

                // Upgrade Unknown health if we now have data to work with
                if (updated.OverallHealth == DiskHealthStatus.Unknown
                    && updated.SmartAttributes.Count == 0)
                {
                    // No raw attributes but Storage namespace responded — assume Good
                    // unless wear is critically low
                    var health = DiskHealthStatus.Good;
                    if (updated.WearLevellingPercent.HasValue)
                    {
                        if (updated.WearLevellingPercent < 10)
                            health = DiskHealthStatus.Bad;
                        else if (updated.WearLevellingPercent < 30)
                            health = DiskHealthStatus.Caution;
                    }

                    updated = updated with { OverallHealth = health };
                }

                disks[idx] = updated;
                idx++;
            }
        }
        catch (ManagementException)
        {
            // Storage namespace may not be accessible without elevation
        }
    }

    // --- Attribute value extraction helpers ---

    private static int? GetAttributeRaw(List<SmartAttribute> attrs, int id)
    {
        var attr = attrs.Find(a => a.Id == id);
        return attr is not null ? (int)attr.RawValue : null;
    }

    private static int? GetAttributeRawInt(List<SmartAttribute> attrs, int id)
    {
        var attr = attrs.Find(a => a.Id == id);
        return attr is not null ? (int)attr.RawValue : null;
    }

    private static long? GetAttributeRawLong(List<SmartAttribute> attrs, int id)
    {
        var attr = attrs.Find(a => a.Id == id);
        return attr is not null ? attr.RawValue : null;
    }

    // --- WMI property helpers ---

    private static string GetString(ManagementBaseObject obj, string property, string fallback) =>
        obj[property]?.ToString() ?? fallback;

    private static long GetLong(ManagementBaseObject obj, string property)
    {
        var val = obj[property];
        return val is not null ? Convert.ToInt64(val) : 0;
    }

    // --- Internal metadata record ---

    private sealed record DiskMeta
    {
        public required string Model { get; init; }
        public required string SerialNumber { get; init; }
        public required string FirmwareRevision { get; init; }
        public required long SizeBytes { get; init; }
        public required string InterfaceType { get; init; }
        public required string MediaType { get; init; }
    }
}

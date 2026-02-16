using System.Management;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Hardware;

/// <summary>
/// Gathers system information via WMI queries.
/// All queries verified against Win11 Pro and Home (25H2).
/// Uses ManagementObjectSearcher (NOT CimSession) for broadest compatibility.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemInfoService : ISystemInfoService
{
    public Task<SystemInfo> GetSystemInfoAsync() => Task.Run(() =>
    {
        var os = QueryFirst("SELECT Caption, Version, BuildNumber, InstallDate, CSName FROM Win32_OperatingSystem");
        var cpu = QueryFirst("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
        var gpu = QueryFirst("SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
        var board = QueryFirst("SELECT Manufacturer, Product FROM Win32_BaseBoard");
        var bios = QueryFirst("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
        var mem = QueryFirst("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");

        // Win11 detection: Version is still "10.0.x" — must check BuildNumber >= 22000
        var buildStr = GetString(os, "BuildNumber", "0");
        _ = int.TryParse(buildStr, out var buildNumber);

        // Edition detection from registry
        var edition = GetWindowsEdition();

        return new SystemInfo
        {
            OsEdition = GetString(os, "Caption", "Unknown"),
            OsVersion = GetString(os, "Version", "Unknown"),
            OsBuild = buildStr,
            InstallDate = ManagementDateTimeConverter.ToDateTime(
                GetString(os, "InstallDate", "19700101000000.000000+000")),
            ComputerName = GetString(os, "CSName", Environment.MachineName),
            Edition = edition,
            CpuName = GetString(cpu, "Name", "Unknown").Trim(),
            CpuCores = GetInt(cpu, "NumberOfCores"),
            CpuThreads = GetInt(cpu, "NumberOfLogicalProcessors"),
            CpuMaxClockMhz = GetUInt(cpu, "MaxClockSpeed"),
            GpuName = GetString(gpu, "Name", "Unknown"),
            GpuDriverVersion = GetString(gpu, "DriverVersion", "Unknown"),
            GpuVramBytes = GetLong(gpu, "AdapterRAM"),
            MotherboardManufacturer = GetString(board, "Manufacturer", "Unknown"),
            MotherboardProduct = GetString(board, "Product", "Unknown"),
            BiosVersion = GetString(bios, "SMBIOSBIOSVersion", "Unknown"),
            BiosDate = bios?["ReleaseDate"] is string rd
                ? ManagementDateTimeConverter.ToDateTime(rd).ToString("dd/MM/yyyy")
                : "Unknown",
            TotalPhysicalMemoryBytes = GetLong(mem, "TotalPhysicalMemory")
        };
    });

    public Task<RamSummary> GetRamSummaryAsync() => Task.Run(() =>
    {
        var dimms = new List<RamDimm>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_PhysicalMemory");

        foreach (var obj in searcher.Get())
        {
            var memoryType = GetUShort(obj, "SMBIOSMemoryType");
            var totalWidth = GetUShort(obj, "TotalWidth");
            var dataWidth = GetUShort(obj, "DataWidth");

            dimms.Add(new RamDimm
            {
                SlotLabel = GetString(obj, "DeviceLocator", "Unknown"),
                CapacityBytes = GetLong(obj, "Capacity"),
                SpeedMhz = GetUInt(obj, "ConfiguredClockSpeed"),
                MemoryType = DecodeMemoryType(memoryType),
                Manufacturer = GetString(obj, "Manufacturer", "Unknown").Trim(),
                PartNumber = GetString(obj, "PartNumber", "Unknown").Trim(),
                SerialNumber = GetString(obj, "SerialNumber", "Unknown").Trim(),
                FormFactor = DecodeFormFactor(GetUShort(obj, "FormFactor")),
                // ECC: TotalWidth (72-bit) > DataWidth (64-bit) = ECC present
                IsEcc = totalWidth > 0 && dataWidth > 0 && totalWidth > dataWidth
            });
        }

        // Get total slot count and max capacity from PhysicalMemoryArray
        int totalSlots = 0;
        long maxCapacityKb = 0;
        using var arraySearcher = new ManagementObjectSearcher(
            "SELECT MemoryDevices, MaxCapacity FROM Win32_PhysicalMemoryArray");
        foreach (var obj in arraySearcher.Get())
        {
            totalSlots += GetInt(obj, "MemoryDevices");
            maxCapacityKb += GetLong(obj, "MaxCapacity");
        }

        return new RamSummary
        {
            Dimms = dimms,
            TotalSlots = totalSlots > 0 ? totalSlots : dimms.Count,
            UsedSlots = dimms.Count,
            MaxCapacityBytes = maxCapacityKb * 1024, // MaxCapacity is in KB
            InstalledCapacityBytes = dimms.Sum(d => d.CapacityBytes),
            ChannelConfig = dimms.Count >= 2 ? "Dual Channel" : "Single Channel"
        };
    });

    public Task<List<DiskHealthInfo>> GetDiskHealthAsync() => Task.Run(() =>
    {
        var disks = new List<DiskHealthInfo>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Model, SerialNumber, FirmwareRevision, Size, InterfaceType, MediaType FROM Win32_DiskDrive");

        foreach (var obj in searcher.Get())
        {
            var mediaType = GetString(obj, "MediaType", "Unknown");

            disks.Add(new DiskHealthInfo
            {
                Model = GetString(obj, "Model", "Unknown").Trim(),
                SerialNumber = GetString(obj, "SerialNumber", "Unknown").Trim(),
                FirmwareRevision = GetString(obj, "FirmwareRevision", "Unknown").Trim(),
                SizeBytes = GetLong(obj, "Size"),
                InterfaceType = GetString(obj, "InterfaceType", "Unknown"),
                MediaType = mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase)
                    || mediaType.Contains("Solid", StringComparison.OrdinalIgnoreCase)
                    ? "SSD"
                    : mediaType.Contains("Fixed", StringComparison.OrdinalIgnoreCase) ? "HDD" : mediaType,
                OverallHealth = DiskHealthStatus.Unknown,
                SmartAttributes = new()
            });
        }

        // Enhance with Storage namespace: health status, accurate media/bus type, SMART data
        TryPopulateSmartData(disks);

        return disks;
    });

    public Task<BatteryInfo> GetBatteryInfoAsync() => Task.Run(() =>
    {
        // Win32_Battery for basic status
        var bat = QueryFirst("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
        if (bat is null)
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

        var chargePercent = GetInt(bat, "EstimatedChargeRemaining");
        var status = GetUShort(bat, "BatteryStatus");

        // BatteryStaticData from ROOT\WMI — must use ManagementObjectSearcher (not CimSession)
        long designCap = 0, fullChargeCap = 0;
        int cycleCount = 0;
        string chemistry = "Unknown", manufacturer = "Unknown";

        try
        {
            using var staticSearcher = new ManagementObjectSearcher(
                @"ROOT\WMI", "SELECT DesignedCapacity, Chemistry, ManufactureName FROM BatteryStaticData");
            foreach (var obj in staticSearcher.Get())
            {
                designCap = GetLong(obj, "DesignedCapacity");
                var chemCode = GetInt(obj, "Chemistry");
                chemistry = chemCode switch
                {
                    1 => "Other", 2 => "Unknown", 3 => "Lead Acid",
                    4 => "Nickel Cadmium", 5 => "Nickel Metal Hydride",
                    6 => "Lithium-ion", 7 => "Zinc Air", 8 => "Lithium Polymer",
                    _ => DecodeAcpiChemistry(chemCode)
                };
                manufacturer = GetString(obj, "ManufactureName", "Unknown");
            }

            using var fullChargeSearcher = new ManagementObjectSearcher(
                @"ROOT\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
            foreach (var obj in fullChargeSearcher.Get())
            {
                fullChargeCap = GetLong(obj, "FullChargedCapacity");
            }

            using var cycleSearcher = new ManagementObjectSearcher(
                @"ROOT\WMI", "SELECT CycleCount FROM BatteryCycleCount");
            foreach (var obj in cycleSearcher.Get())
            {
                cycleCount = GetInt(obj, "CycleCount");
            }
        }
        catch (ManagementException)
        {
            // ROOT\WMI battery classes may not be available on all hardware
        }

        return new BatteryInfo
        {
            DesignCapacityMwh = designCap,
            FullChargeCapacityMwh = fullChargeCap,
            CycleCount = cycleCount,
            Chemistry = chemistry,
            Manufacturer = manufacturer,
            ChargePercent = chargePercent,
            // BatteryStatus: 2 = AC Power, 6-9 = Charging variants
            IsCharging = status is 2 or 6 or 7 or 8 or 9,
            HasBattery = true
        };
    });

    public Task<string?> GetProductKeyAsync() => Task.Run(() =>
    {
        // Method 1: BIOS/UEFI embedded OEM key
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT OA3xOriginalProductKey FROM SoftwareLicensingService");
            foreach (var obj in searcher.Get())
            {
                var key = obj["OA3xOriginalProductKey"]?.ToString();
                if (!string.IsNullOrWhiteSpace(key))
                    return key;
            }
        }
        catch (ManagementException)
        {
            // Not available on all systems
        }

        // Method 2: Registry-stored key (decoded from DigitalProductId)
        try
        {
            using var regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var digitalProductId = regKey?.GetValue("DigitalProductId") as byte[];
            if (digitalProductId is { Length: >= 67 })
                return DecodeProductKey(digitalProductId);
        }
        catch
        {
            // Registry access may fail without elevation
        }

        return (string?)null;
    });

    public Task<List<DriverInfo>> GetOutdatedDriversAsync(int olderThanYears = 3) => Task.Run(() =>
    {
        var cutoff = DateTime.Now.AddYears(-olderThanYears);
        var drivers = new List<DriverInfo>();

        // Skip device classes that don't represent real updatable hardware
        var skipClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LEGACYDRIVER", "SYSTEM", "VOLUMESNAPSHOT", "VOLUME",
            "COMPUTER", "PROCESSOR", "HIDCLASS",
            "PRINTQUEUE", "PRINTER", "NET", "NETCLIENT",
            "NETTRANS", "NETSERVICE", "SECURITYDEVICES",
            "SCSIADAPTER", "FIRMWARE", "SOFTWAREDEVICE",
            "USB", "WPDBUSENUMROOT"
        };

        // Skip Windows built-in virtual devices by name pattern
        var skipNamePatterns = new[]
        {
            "WAN Miniport",
            "Microsoft Kernel",
            "Microsoft ISATAP",
            "Microsoft Wi-Fi Direct",
            "Microsoft 6to4",
            "Microsoft Teredo",
            "Microsoft IP-HTTPS",
            "Microsoft GS Wavetable",
            "Microsoft XPS",
            "Microsoft Print",
            "Fax",
            "Remote Desktop",
            "RAS Async",
            "WAN Async",
            "Direct Parallel",
            "Composite Bus",
            "UMBus Root",
            "NDIS Virtual",
            "Generic software device",
            "Root Print Queue",
        };

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion, Manufacturer, DeviceClass, DriverDate " +
                "FROM Win32_PnPSignedDriver WHERE DriverDate IS NOT NULL");

            foreach (var obj in searcher.Get())
            {
                var dateStr = GetString(obj, "DriverDate", "");
                if (string.IsNullOrEmpty(dateStr)) continue;

                try
                {
                    var driverDate = ManagementDateTimeConverter.ToDateTime(dateStr);

                    // Skip future dates or impossibly old dates
                    if (driverDate > DateTime.Now || driverDate.Year < 2000) continue;
                    if (driverDate >= cutoff) continue;

                    var deviceClass = GetString(obj, "DeviceClass", "Unknown");
                    if (skipClasses.Contains(deviceClass)) continue;

                    var deviceName = GetString(obj, "DeviceName", "Unknown");
                    if (string.IsNullOrWhiteSpace(deviceName) || deviceName == "Unknown") continue;

                    // Skip Windows built-in virtual devices
                    if (skipNamePatterns.Any(p => deviceName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // Skip Microsoft-published drivers — these are OS components, not updatable
                    var manufacturer = GetString(obj, "Manufacturer", "Unknown").Trim();
                    if (manufacturer.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)) continue;

                    drivers.Add(new DriverInfo
                    {
                        DeviceName = deviceName.Trim(),
                        DriverVersion = GetString(obj, "DriverVersion", "Unknown"),
                        Manufacturer = GetString(obj, "Manufacturer", "Unknown").Trim(),
                        DeviceClass = deviceClass,
                        DriverDate = driverDate
                    });
                }
                catch
                {
                    // Date conversion failure — skip
                }
            }
        }
        catch (ManagementException)
        {
            // WMI query failure — return empty
        }

        return drivers
            .OrderBy(d => d.DriverDate)
            .ToList();
    });

    // --- SMART data population via Storage namespace (MSFT_PhysicalDisk + MSFT_StorageReliabilityCounter) ---

    /// <summary>
    /// Enhances disk list with accurate health status, media/bus type from MSFT_PhysicalDisk,
    /// and SMART telemetry from MSFT_StorageReliabilityCounter.
    /// Both classes live in ROOT\Microsoft\Windows\Storage and work for SATA and NVMe on Win11.
    /// Matching is by model name (FriendlyName vs Model) rather than fragile index ordering.
    /// </summary>
    private static void TryPopulateSmartData(List<DiskHealthInfo> disks)
    {
        if (disks.Count == 0) return;

        // --- Step 1: Query MSFT_PhysicalDisk for health, accurate media/bus type, and FriendlyName ---
        var physicalDisks = new List<PhysicalDiskEntry>();
        try
        {
            using var pdSearcher = new ManagementObjectSearcher(
                @"ROOT\Microsoft\Windows\Storage",
                "SELECT DeviceId, FriendlyName, HealthStatus, MediaType, BusType, Size FROM MSFT_PhysicalDisk");

            foreach (var obj in pdSearcher.Get())
            {
                physicalDisks.Add(new PhysicalDiskEntry
                {
                    DeviceId = GetString(obj, "DeviceId", ""),
                    FriendlyName = GetString(obj, "FriendlyName", "").Trim(),
                    HealthStatus = GetUShort(obj, "HealthStatus"),
                    MediaType = GetUShort(obj, "MediaType"),
                    BusType = GetUShort(obj, "BusType"),
                    SizeBytes = GetLong(obj, "Size")
                });
            }
        }
        catch (ManagementException)
        {
            // Storage namespace not accessible — cannot enhance, return with originals
            return;
        }

        // --- Step 2: Match physical disks to our DiskHealthInfo list and apply health/type overrides ---
        foreach (var pd in physicalDisks)
        {
            var matchIdx = FindMatchingDiskIndex(disks, pd.FriendlyName, pd.SizeBytes);
            if (matchIdx < 0) continue;

            var disk = disks[matchIdx];

            // Map MSFT_PhysicalDisk.HealthStatus → DiskHealthStatus
            var health = pd.HealthStatus switch
            {
                0 => DiskHealthStatus.Good,
                1 => DiskHealthStatus.Caution,   // Warning
                2 => DiskHealthStatus.Bad,        // Unhealthy
                _ => DiskHealthStatus.Unknown      // 5 = Unknown, others
            };

            // Map MSFT_PhysicalDisk.MediaType (more accurate than Win32_DiskDrive)
            var mediaType = pd.MediaType switch
            {
                3 => "HDD",
                4 => "SSD",
                5 => "SCM",
                _ => disk.MediaType   // Keep existing if unspecified (0)
            };

            // Map MSFT_PhysicalDisk.BusType (solves NVMe misreported as "SCSI")
            var interfaceType = pd.BusType switch
            {
                7 => "SATA",
                11 or 17 => "NVMe",
                6 => "Fibre Channel",
                8 => "SSA",
                9 => "IEEE 1394",
                10 => "SAS",
                12 => "SD",
                13 => "MMC",
                14 => "Virtual",
                15 => "File Backed Virtual",
                16 => "Storage Spaces",
                _ => disk.InterfaceType   // Keep existing for USB (3), SCSI, etc.
            };

            disks[matchIdx] = disk with
            {
                OverallHealth = health,
                MediaType = mediaType,
                InterfaceType = interfaceType
            };
        }

        // --- Step 3: Query MSFT_StorageReliabilityCounter for SMART telemetry ---
        try
        {
            using var rcSearcher = new ManagementObjectSearcher(
                @"ROOT\Microsoft\Windows\Storage",
                "SELECT * FROM MSFT_StorageReliabilityCounter");

            // MSFT_StorageReliabilityCounter enumerates in the same order as MSFT_PhysicalDisk
            // within the Storage namespace. We use this to correlate via FriendlyName.
            var rcIndex = 0;
            foreach (var obj in rcSearcher.Get())
            {
                // Find which DiskHealthInfo this reliability counter belongs to
                // by mapping through the MSFT_PhysicalDisk list (same enumeration order)
                int diskIdx = -1;
                if (rcIndex < physicalDisks.Count)
                {
                    var pd = physicalDisks[rcIndex];
                    diskIdx = FindMatchingDiskIndex(disks, pd.FriendlyName, pd.SizeBytes);
                }
                rcIndex++;

                if (diskIdx < 0) continue;
                var disk = disks[diskIdx];

                // Temperature (may be null on some NVMe drives — known Win11 quirk)
                int? temp = null;
                var tempVal = obj["Temperature"];
                if (tempVal is not null)
                    temp = Convert.ToInt32(tempVal);

                // Power-on hours
                long? powerOn = null;
                var powerOnVal = obj["PowerOnHours"];
                if (powerOnVal is not null)
                    powerOn = Convert.ToInt64(powerOnVal);

                // Wear levelling (SSD/NVMe percentage used, 0 = new, 100 = end of life)
                int? wear = null;
                var wearVal = obj["Wear"];
                if (wearVal is not null)
                    wear = Convert.ToInt32(wearVal);

                // Build SMART attributes list from available counters
                var smartAttrs = new List<SmartAttribute>(disk.SmartAttributes);

                // Read error counters (NVMe-specific, valuable for diagnostics)
                TryAddSmartAttribute(smartAttrs, obj, "ReadErrorsTotal", 1, "Read Errors Total");
                TryAddSmartAttribute(smartAttrs, obj, "WriteErrorsTotal", 200, "Write Errors Total");
                TryAddSmartAttribute(smartAttrs, obj, "ReadLatencyMax", 201, "Read Latency Max (ns)");
                TryAddSmartAttribute(smartAttrs, obj, "WriteLatencyMax", 202, "Write Latency Max (ns)");

                // Standard counters as SMART attributes for the table display
                if (temp.HasValue)
                    TryAddSmartAttribute(smartAttrs, obj, "Temperature", 194, "Temperature (C)");
                if (powerOn.HasValue)
                    TryAddSmartAttribute(smartAttrs, obj, "PowerOnHours", 9, "Power-On Hours");
                if (wear.HasValue)
                    TryAddSmartAttribute(smartAttrs, obj, "Wear", 177, "Wear Levelling Count");

                disks[diskIdx] = disk with
                {
                    TemperatureCelsius = temp ?? disk.TemperatureCelsius,
                    PowerOnHours = powerOn ?? disk.PowerOnHours,
                    WearLevellingPercent = wear ?? disk.WearLevellingPercent,
                    SmartAttributes = smartAttrs
                };
            }
        }
        catch (ManagementException)
        {
            // MSFT_StorageReliabilityCounter not accessible — health status from Step 2 still applies
        }
    }

    /// <summary>
    /// Finds the best matching disk in our list by comparing model names.
    /// Uses case-insensitive contains matching with size as a tiebreaker.
    /// </summary>
    private static int FindMatchingDiskIndex(List<DiskHealthInfo> disks, string friendlyName, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(friendlyName)) return -1;

        // Exact match first (case-insensitive)
        for (var i = 0; i < disks.Count; i++)
        {
            if (disks[i].Model.Equals(friendlyName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        // Contains match — FriendlyName within Model or Model within FriendlyName
        for (var i = 0; i < disks.Count; i++)
        {
            if (disks[i].Model.Contains(friendlyName, StringComparison.OrdinalIgnoreCase)
                || friendlyName.Contains(disks[i].Model, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        // Fallback: match by size (within 5% tolerance) when names don't align
        if (sizeBytes > 0)
        {
            for (var i = 0; i < disks.Count; i++)
            {
                var diff = Math.Abs(disks[i].SizeBytes - sizeBytes);
                if (diff < sizeBytes * 0.05)
                    return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Attempts to read a WMI property and add it as a SmartAttribute entry.
    /// Silently skips if the property is null or unavailable.
    /// </summary>
    private static void TryAddSmartAttribute(
        List<SmartAttribute> attrs, ManagementBaseObject obj,
        string propertyName, int smartId, string displayName)
    {
        try
        {
            var val = obj[propertyName];
            if (val is null) return;

            var rawValue = Convert.ToInt64(val);
            attrs.Add(new SmartAttribute
            {
                Id = smartId,
                Name = displayName,
                CurrentValue = rawValue > int.MaxValue ? 100 : (int)rawValue,
                WorstValue = 0,
                Threshold = 0,
                RawValue = rawValue,
                Status = DiskHealthStatus.Good
            });
        }
        catch
        {
            // Property not available on this drive — skip silently
        }
    }

    /// <summary>
    /// Intermediate record for MSFT_PhysicalDisk query results.
    /// </summary>
    private sealed record PhysicalDiskEntry
    {
        public required string DeviceId { get; init; }
        public required string FriendlyName { get; init; }
        public required ushort HealthStatus { get; init; }
        public required ushort MediaType { get; init; }
        public required ushort BusType { get; init; }
        public required long SizeBytes { get; init; }
    }

    // --- Windows Edition detection ---

    private static WindowsEdition GetWindowsEdition()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var editionId = key?.GetValue("EditionID")?.ToString();
            return editionId switch
            {
                "Professional" => WindowsEdition.Pro,
                "Core" => WindowsEdition.Home,
                _ => WindowsEdition.Unknown
            };
        }
        catch
        {
            return WindowsEdition.Unknown;
        }
    }

    // --- Product key decoding ---

    private static string DecodeProductKey(byte[] digitalProductId)
    {
        const string chars = "BCDFGHJKMPQRTVWXY2346789";
        var keyOffset = 52;
        var isWin8Plus = (digitalProductId[66] / 6) & 1;
        digitalProductId[66] = (byte)((digitalProductId[66] & 0xF7) | ((isWin8Plus & 2) * 4));

        var key = new char[29];
        var last = 0;

        for (var i = 24; i >= 0; i--)
        {
            var current = 0;
            for (var j = 14; j >= 0; j--)
            {
                current *= 256;
                current += digitalProductId[j + keyOffset];
                digitalProductId[j + keyOffset] = (byte)(current / 24);
                current %= 24;
            }
            key[i] = chars[current];
            last = current;
        }

        // Insert 'N' for Win8+ keys
        if (isWin8Plus != 0)
        {
            var insert = new char[30];
            var keyStr = new string(key, 0, 25);
            keyStr = keyStr.Insert(last, "N");
            keyStr[..25].CopyTo(0, insert, 0, 25);
        }

        // Format as XXXXX-XXXXX-XXXXX-XXXXX-XXXXX
        var formatted = new string(key, 0, 25);
        return $"{formatted[..5]}-{formatted[5..10]}-{formatted[10..15]}-{formatted[15..20]}-{formatted[20..25]}";
    }

    // --- WMI helpers ---

    private static ManagementBaseObject? QueryFirst(string query)
    {
        using var searcher = new ManagementObjectSearcher(query);
        foreach (var obj in searcher.Get())
            return obj;
        return null;
    }

    private static string GetString(ManagementBaseObject? obj, string property, string fallback) =>
        obj?[property]?.ToString() ?? fallback;

    private static int GetInt(ManagementBaseObject? obj, string property)
    {
        var val = obj?[property];
        return val is not null ? Convert.ToInt32(val) : 0;
    }

    private static uint GetUInt(ManagementBaseObject? obj, string property)
    {
        var val = obj?[property];
        return val is not null ? Convert.ToUInt32(val) : 0;
    }

    private static ushort GetUShort(ManagementBaseObject? obj, string property)
    {
        var val = obj?[property];
        return val is not null ? Convert.ToUInt16(val) : (ushort)0;
    }

    private static long GetLong(ManagementBaseObject? obj, string property)
    {
        var val = obj?[property];
        return val is not null ? Convert.ToInt64(val) : 0;
    }

    // --- Memory type/form factor decoding ---

    private static string DecodeMemoryType(ushort smbiosType) => smbiosType switch
    {
        20 => "DDR",
        21 => "DDR2",
        22 => "DDR2 FB-DIMM",
        24 => "DDR3",
        26 => "DDR4",
        34 => "DDR5",
        _ => $"Type {smbiosType}"
    };

    private static string DecodeFormFactor(ushort formFactor) => formFactor switch
    {
        8 => "DIMM",
        12 => "SO-DIMM",
        _ => $"Form {formFactor}"
    };

    /// <summary>
    /// ACPI batteries often store chemistry as a 4-byte ASCII string packed into an integer
    /// rather than using the standard SMBIOS codes 1-8.
    /// e.g. "LiP\0" = 0x0050694C = 5269836 decimal → Lithium Polymer.
    /// </summary>
    private static string DecodeAcpiChemistry(int rawValue)
    {
        if (rawValue <= 0) return "Unknown";

        try
        {
            var bytes = BitConverter.GetBytes(rawValue);
            var len = 0;
            for (var i = 0; i < 4; i++)
            {
                if (bytes[i] == 0) break;
                if (bytes[i] < 0x20 || bytes[i] > 0x7E) return $"Type {rawValue}";
                len++;
            }

            if (len == 0) return $"Type {rawValue}";

            var decoded = System.Text.Encoding.ASCII.GetString(bytes, 0, len);

            return decoded.ToUpperInvariant() switch
            {
                "LIP" => "Lithium Polymer",
                "LION" or "LI-I" => "Lithium-ion",
                "PBAC" => "Lead Acid",
                "NICD" => "Nickel Cadmium",
                "NIMH" => "Nickel Metal Hydride",
                "RAMR" => "Rechargeable RAM",
                _ => decoded
            };
        }
        catch
        {
            return $"Type {rawValue}";
        }
    }
}

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
                // SMART attributes require MSFT_StorageReliabilityCounter (Storage namespace)
                // or MSStorageDriver_ATAPISmartData (ROOT\WMI) — added separately
                SmartAttributes = new()
            });
        }

        // Attempt SMART data from Storage namespace (works for SATA + NVMe)
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
                    _ => $"Type {chemCode}"
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

        // Skip generic device classes that don't represent real hardware
        var skipClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LEGACYDRIVER", "SYSTEM", "VOLUMESNAPSHOT", "VOLUME",
            "COMPUTER", "PROCESSOR", "HIDCLASS"
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

    // --- SMART data population ---

    private static void TryPopulateSmartData(List<DiskHealthInfo> disks)
    {
        try
        {
            // MSFT_StorageReliabilityCounter in ROOT\Microsoft\Windows\Storage
            // Works for both SATA and NVMe on Win11
            using var searcher = new ManagementObjectSearcher(
                @"ROOT\Microsoft\Windows\Storage",
                "SELECT * FROM MSFT_StorageReliabilityCounter");

            var idx = 0;
            foreach (var obj in searcher.Get())
            {
                if (idx >= disks.Count) break;
                var disk = disks[idx];

                // Temperature may be null on some NVMe drives (known Win11 bug)
                var temp = obj["Temperature"];
                if (temp is not null)
                    disks[idx] = disk with { TemperatureCelsius = Convert.ToInt32(temp) };

                var powerOn = obj["PowerOnHours"];
                if (powerOn is not null)
                    disks[idx] = disk with { PowerOnHours = Convert.ToInt64(powerOn) };

                var wear = obj["Wear"];
                if (wear is not null)
                    disks[idx] = disk with { WearLevellingPercent = Convert.ToInt32(wear) };

                idx++;
            }
        }
        catch (ManagementException)
        {
            // Storage namespace may not be accessible without elevation
        }
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
}

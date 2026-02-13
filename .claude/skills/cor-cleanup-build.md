---
name: cor-cleanup-build
description: Build and develop the COR Cleanup Windows multi-tool — .NET 8 WPF application with MVVM architecture
triggers:
  - build
  - implement
  - create service
  - add feature
  - new view
  - scaffold
  - cor cleanup
category: development
---

# COR Cleanup Build Skill

## Project Context

COR Cleanup is a .NET 8 + WPF desktop application for Windows 11 (Pro and Home). It is an all-in-one MSP technician tool replacing CCleaner, Speccy, CrystalDiskInfo, Advanced IP Scanner, Revo Uninstaller, BlueScreenView, and more.

**Stack**: C# / .NET 8 / WPF (Fluent theme) / MVVM / PowerShell 5.1 scripts
**Target**: Windows 11 x64 (Pro + Home)
**Dev environment**: macOS (build via GitHub Actions `windows-latest`)

## Architecture Pattern

Every feature follows the same MVVM pattern:

```
View (XAML)  →  ViewModel (C#)  →  Service (C#)
   UI only       Binding + commands    Business logic + Windows APIs
```

### Creating a New Feature

1. **Service first** — Business logic in `Services/` subfolder. No UI dependencies. Pure C# with WMI/Registry/PowerShell calls.
2. **Model** — Data classes in `Models/`. Simple POCOs with properties.
3. **ViewModel** — In `ViewModels/`. Implements `INotifyPropertyChanged`. Calls service methods. Exposes `ObservableCollection<T>` and `ICommand` for the view.
4. **View** — XAML in `Views/`. Binds to ViewModel. Uses shared controls from `Controls/`.

### Service Patterns

**WMI Query Pattern** (for hardware info, drivers, battery, etc.):
```csharp
public class WmiService
{
    public IEnumerable<RamModule> GetRamModules()
    {
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
        foreach (ManagementObject obj in searcher.Get())
        {
            yield return new RamModule
            {
                Slot = obj["DeviceLocator"]?.ToString() ?? "Unknown",
                CapacityGB = Convert.ToUInt64(obj["Capacity"]) / 1_073_741_824,
                SpeedMHz = Convert.ToUInt32(obj["ConfiguredClockSpeed"]),
                Manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "Unknown",
                PartNumber = obj["PartNumber"]?.ToString()?.Trim() ?? "",
                MemoryType = DecodeMemoryType(Convert.ToUInt16(obj["SMBIOSMemoryType"]))
            };
        }
    }
}
```

**Registry Pattern** (for cleanup scanning, uninstall info, etc.):
```csharp
using Microsoft.Win32;

public IEnumerable<InstalledProgram> GetInstalledPrograms()
{
    var paths = new[]
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    foreach (var path in paths)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        if (key == null) continue;

        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(subKeyName);
            var name = subKey?.GetValue("DisplayName")?.ToString();
            if (string.IsNullOrEmpty(name)) continue;

            yield return new InstalledProgram
            {
                Name = name,
                Publisher = subKey.GetValue("Publisher")?.ToString() ?? "",
                Version = subKey.GetValue("DisplayVersion")?.ToString() ?? "",
                InstallDate = subKey.GetValue("InstallDate")?.ToString() ?? "",
                EstimatedSizeKB = subKey.GetValue("EstimatedSize") as int? ?? 0,
                UninstallString = subKey.GetValue("UninstallString")?.ToString() ?? ""
            };
        }
    }
}
```

**PowerShell Execution Pattern** (for operations simpler in PS than pure C#):
```csharp
public async Task<string> RunPowerShellAsync(string scriptPath, params string[] args)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = "powershell.exe",
        Arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{scriptPath}\" {string.Join(" ", args)}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var process = Process.Start(startInfo);
    var output = await process!.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();
    return output;
}
```

**Async with Progress Pattern** (for long-running operations like cleanup, scanning):
```csharp
public async Task CleanTempFilesAsync(IProgress<CleanupProgress> progress, CancellationToken ct)
{
    var locations = GetCleanupLocations();
    long totalReclaimed = 0;

    for (int i = 0; i < locations.Count; i++)
    {
        ct.ThrowIfCancellationRequested();

        var reclaimed = await CleanLocationAsync(locations[i], ct);
        totalReclaimed += reclaimed;

        progress.Report(new CleanupProgress
        {
            CurrentItem = locations[i].Name,
            PercentComplete = (i + 1) * 100 / locations.Count,
            BytesReclaimed = totalReclaimed
        });
    }
}
```

**Network Operations Pattern** (for ping, scanner, DNS):
```csharp
public async IAsyncEnumerable<PingResult> ContinuousPingAsync(
    string target, [EnumeratorCancellation] CancellationToken ct)
{
    using var ping = new Ping();
    while (!ct.IsCancellationRequested)
    {
        var reply = await ping.SendPingAsync(target, 1000);
        yield return new PingResult
        {
            Timestamp = DateTime.Now,
            Status = reply.Status,
            RoundtripMs = reply.RoundtripTime,
            Ttl = reply.Options?.Ttl ?? 0
        };
        await Task.Delay(1000, ct);
    }
}
```

## Key .NET APIs by Feature

| Feature | Primary API | Namespace |
|---------|-------------|-----------|
| Registry | `Registry`, `RegistryKey` | `Microsoft.Win32` |
| WMI (hardware) | `ManagementObjectSearcher` | `System.Management` |
| Services | `ServiceController` | `System.ServiceProcess` |
| Processes | `Process` | `System.Diagnostics` |
| Ping | `Ping` | `System.Net.NetworkInformation` |
| DNS | `Dns.GetHostEntry`, raw UDP for record types | `System.Net`, `System.Net.Sockets` |
| TCP ports | `TcpClient.ConnectAsync` | `System.Net.Sockets` |
| File ops | `Directory`, `File`, `FileInfo` | `System.IO` |
| Hashing | `SHA256.HashData` | `System.Security.Cryptography` |
| Event Log | `EventLog`, `EventLogReader` | `System.Diagnostics.Eventing.Reader` |
| Firewall | COM interop `HNetCfg.FwPolicy2` or `netsh` via PowerShell | COM / PowerShell |

## WMI Classes Reference

| Data | WMI Class | Key Properties |
|------|-----------|----------------|
| RAM DIMMs | `Win32_PhysicalMemory` | DeviceLocator, Capacity, Speed, SMBIOSMemoryType, Manufacturer, PartNumber |
| RAM slots | `Win32_PhysicalMemoryArray` | MaxCapacity, MemoryDevices |
| CPU | `Win32_Processor` | Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed |
| GPU | `Win32_VideoController` | Name, DriverVersion, AdapterRAM |
| Motherboard | `Win32_BaseBoard` | Manufacturer, Product |
| BIOS | `Win32_BIOS` | SMBIOSBIOSVersion, ReleaseDate |
| Disks | `Win32_DiskDrive` | Model, Size, MediaType, InterfaceType |
| S.M.A.R.T. | `MSStorageDriver_ATAPISmartData` (ROOT\WMI) | VendorSpecific (raw byte array) |
| NVMe health | `MSFT_PhysicalDisk` (ROOT\Microsoft\Windows\Storage) | MediaType, SpindleSpeed, HealthStatus |
| Battery | `Win32_Battery` | EstimatedChargeRemaining, DesignVoltage |
| Battery detail | `BatteryStaticData` + `BatteryFullChargedCapacity` (ROOT\WMI) | DesignedCapacity, FullChargedCapacity |
| Drivers | `Win32_PnPSignedDriver` | DeviceName, DriverVersion, DriverDate, IsSigned, Manufacturer |
| Network adapters | `Win32_NetworkAdapterConfiguration` | IPAddress, MACAddress, DefaultIPGateway, DNSServerSearchOrder |
| OS info | `Win32_OperatingSystem` | Caption, Version, BuildNumber, InstallDate |
| Product key | `SoftwareLicensingService` | OA3xOriginalProductKey |
| Startup | `Win32_StartupCommand` | Command, Location, User |
| Printers | `Win32_Printer` | Name, PortName, PrinterStatus, Default |

## UI Conventions

- **Dark mode default** — Fluent theme with dark/light toggle
- **Sidebar navigation** — 7 sections, each with sub-tabs within the view
- **DataTable control** — Reuse for ALL tabular data (network scan, drivers, services, startup, software, event log, firewall rules)
- **ProgressPanel control** — Reuse for ALL long-running ops (cleanup, registry scan, disk analysis, SFC/DISM)
- **ConfirmDialog** — Before ANY destructive operation (delete, registry fix, uninstall, shred)
- **Toast notifications** — For completed background operations
- **UK English** — All user-facing strings: "colour", "organisation", "optimisation", "analyse"
- **Export everywhere** — Every table view has CSV export. Reports export HTML/PDF.

## Safety Rules

- Whitelist-only deletion — enumerate known-safe paths, never glob system directories
- Registry backup (`.reg` file) BEFORE any modification
- Uninstall leftovers → Recycle Bin (not permanent delete)
- File shredder requires explicit confirmation per operation
- All operations support `CancellationToken` for user abort
- Log all operations to `%APPDATA%\COR Cleanup\Logs\{date}.log`
- Portable mode: detect `portable.flag` file next to .exe → store data alongside

## Build & CI/CD

```yaml
# .github/workflows/build-windows.yml
name: Build COR Cleanup
on: [push, pull_request]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet restore
      - run: dotnet build --configuration Release --no-restore
      - run: dotnet test --no-restore --verbosity normal
      - run: dotnet publish -c Release -r win-x64 --self-contained -o ./publish
      - uses: actions/upload-artifact@v4
        with:
          name: CORCleanup-win-x64
          path: ./publish/
```

## Win11 Pro vs Home

Detect early via registry `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\EditionID`:
- `"Professional"` = Pro
- `"Core"` = Home

Gate features: BitLocker, Group Policy, Hyper-V, Remote Desktop → greyed out on Home with tooltip.

## NuGet Packages (Recommended)

- `CommunityToolkit.Mvvm` — MVVM helpers (ObservableObject, RelayCommand)
- `System.Management` — WMI access
- `DnsClient` — Advanced DNS lookups (record types beyond basic)
- `SharpPcap` or `PacketDotNet` — Raw packet capture for ARP scanning (if needed)
- `LiveChartsCore` or `ScottPlot` — Charts for ping latency, disk usage, speed test
- `QuestPDF` — PDF report generation
- `Ookla.SpeedTest.Net` — Speed test API (or use Cloudflare endpoint)

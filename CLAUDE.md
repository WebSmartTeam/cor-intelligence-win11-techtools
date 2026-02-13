# COR Cleanup — Windows Multi-Tool

## Project Identity
- **Product**: COR Cleanup — The One Tool for Every Job
- **Company**: COR Intelligence (COR Solutions Services Ltd)
- **Targets**: Windows 11 Pro and Home editions
- **Purpose**: All-in-one system cleanup, network diagnostics, hardware info, uninstaller, and admin tool
- **Audience**: MSP technicians, IT support, power users — replaces 7+ separate utility downloads
- **Replaces**: CCleaner + Speccy + CrystalDiskInfo + Advanced IP Scanner + Revo Uninstaller + BlueScreenView + Wi-Fi password scripts
- **Distribution**: Standalone installer, portable mode (runs from USB), COR Intelligence branded

## Tech Stack

- **Framework**: .NET 8 + WPF (Windows Presentation Foundation) with Fluent theme
- **Language**: C#
- **UI**: WPF with Windows 11 Fluent Design (dark mode primary)
- **Build**: MSBuild, GitHub Actions CI/CD on `windows-latest` runner
- **Installer**: NSIS or WiX for `.exe` installer + portable `.zip` option
- **System Access**: WMI, Windows Registry API, PowerShell 5.1 scripts where needed

### Why .NET 8 + WPF (Evidence-Based Decision)

**What successful Windows admin tools actually use:**
| Tool | Stack | Downloads |
|------|-------|-----------|
| PowerToys | C# + .NET + WinUI 3 | 100M+ |
| TreeSize | C# + .NET | Commercial |
| DevToys 2.0 | C# + .NET + Blazor/WPF | Popular |
| CCleaner | C++ (Visual C++) | 2.5B+ |
| Sysinternals | C/C++ (Win32) | Industry standard |

**Zero successful Windows admin tools use Tauri or Electron.**

**Why .NET wins for this use case:**
- Every requirement maps to a built-in .NET API — no bridging, no workarounds
- `Microsoft.Win32.Registry` — registry read/write/backup (built-in)
- `System.Management` (WMI) — RAM info, drivers, S.M.A.R.T., hardware (simple queries)
- `System.ServiceProcess.ServiceController` — services management (built-in)
- `System.Diagnostics.Process` — process kill/restart (built-in)
- `System.Net.NetworkInformation.Ping` — network operations (built-in)
- UAC elevation = one line in app manifest
- WPF Fluent theme (.NET 9+) gives native Windows 11 look
- ~30-80MB self-contained bundle (vs ~5MB Tauri, ~150MB+ Electron)
- 15+ years of WPF maturity, massive NuGet ecosystem

**Development note:** Develop on macOS, build via GitHub Actions `windows-latest` runner. Test on Windows 11 VMs (Pro + Home). This is standard practice — PowerToys does the same.

---

## Feature Modules (7 Sections)

The app has a sidebar with 7 main sections. Each is a tab in the navigation.

---

### 1. Network & Diagnostics

#### Continuous Ping (ping -t style)
- **Quick button**: "Ping Google" → continuous ping to `www.google.co.uk`
- **Custom target**: Free text input for any domain or IP address
- **Behaviour**: Continuous (like `ping -t`) — runs until Stop clicked
- **Display**: Live scrolling results — timestamp, response time (ms), TTL
- **Stats bar**: Min/Max/Avg latency, packet loss %, total sent/received
- **Visual**: Real-time latency graph (sparkline chart)
- **Export**: Copy results to clipboard or save to text file

#### Traceroute (visual)
- Hop-by-hop path to target with latency per hop
- Highlight slow hops (>100ms) in amber, timeouts in red
- Show IP, hostname, and estimated geo-location per hop
- "Where is the latency?" — the natural follow-up to ping

#### Network Scanner (Advanced IP Scanner style)
- **Auto-detect**: Local subnet from active adapter (e.g., 192.168.1.0/24)
- **Manual range**: Custom IP range input, saved favourite ranges
- **Scan results table**:
  - IP address, hostname (reverse DNS), MAC address
  - Manufacturer/vendor (MAC OUI lookup)
  - Status (online/offline), response time
  - Shared folders detected (SMB shares)
- **Features**:
  - Sort/filter by any column
  - Export to CSV
  - Rescan individual devices
  - Right-click: ping, open in browser, copy IP/MAC, RDP connect, Wake-on-LAN
- **Method**: ARP scan + ICMP ping sweep

#### DNS Lookup
- Query any domain for A, AAAA, MX, CNAME, TXT, NS, SOA records
- Choose DNS server: System default, Google (8.8.8.8), Cloudflare (1.1.1.1), custom
- "Email not working?" → check MX records instantly
- Propagation check: query same domain across multiple DNS servers simultaneously
- Copy results to clipboard

#### Port Scanner / Port Checker
- **Remote**: Check if specific ports are open on a target host (single port, range, or common presets: HTTP 80/443, RDP 3389, SSH 22, SMB 445, FTP 21)
- **Local**: What's listening on this machine — process name, PID, local port, protocol (TCPView equivalent)
- **Presets**: "Web Server", "Remote Access", "Email", "Database" port groups
- Identify which process owns each port

#### Active Connections Monitor (TCPView style)
- Real-time view of all network connections
- Process name, local address:port, remote address:port, state, bytes sent/received
- Spot bandwidth hogs and suspicious outbound connections
- Auto-refresh with configurable interval
- Kill connection option (terminate owning process)

#### Speed Test
- Built-in internet bandwidth test — no browser needed
- Download speed, upload speed, latency, jitter
- Server selection (nearest or manual)
- History of previous tests with graph over time

#### Subnet Calculator
- Enter IP/CIDR → get network range, broadcast address, usable host range, subnet mask
- Or enter two IPs → check if they're on the same subnet
- VLSM/supernet calculator
- Common subnets reference table

#### Wake-on-LAN (v1.1)
- Send magic packet from scanner results
- Batch WoL for multiple machines

#### Quick Network Info
- Active adapter name, IP, subnet, gateway, DNS servers
- Public IP (single API call)
- Connection type (Wi-Fi/Ethernet), link speed
- Wi-Fi signal strength (if wireless)

---

### 2. System Cleanup (CCleaner-style)

#### Safe Defaults Philosophy
**The golden rule**: Clean aggressively on system junk, conservatively on user data. Press "Clean" without fear of disrupting the user's workflow.

#### Windows Cleanup (all ON by default)
- Windows Temp (`%TEMP%`, `C:\Windows\Temp`)
- Windows Update cleanup (superseded updates, delivery optimisation cache)
- Windows Error Reporting (`C:\ProgramData\Microsoft\Windows\WER`)
- Recycle Bin contents
- Prefetch files (`C:\Windows\Prefetch`)
- Thumbnail cache
- DNS cache flush
- Font cache
- Windows Installer temp files
- Old Windows Update logs
- Memory dump files (`C:\Windows\MEMORY.DMP`, minidumps — after we've read crash data)
- Windows log files (old CBS, DISM, setup logs)

#### Browser Cleanup (safe defaults — preserve user experience)
**ON by default** (safe):
- Browser cache/temporary internet files
- Download history (not the actual downloaded files)
- Expired session data

**OFF by default** (protect user workflow):
- Browser history — **OFF** (Pete: "I always have to untick this in CCleaner")
- Saved passwords/logins — **OFF** (never delete)
- Cached logins/active sessions — **OFF** (don't force re-login)
- Cookies — **OFF** (clearing = logging out of everything)
- Autofill form data — **OFF** (some users rely on this)
- Bookmarks — **OFF** (never delete)

**Supported browsers**: Chrome, Edge, Firefox, Brave (auto-detect installed)

#### Application Cleanup
- Adobe temp files
- Microsoft Office temp/recent files (not recent docs list)
- Windows Media Player cache
- Java cache
- Other common app temp locations (detect installed apps)

#### Pre-Clean Behaviour
- **Detect running apps**: Before cleaning, check for running browsers and OneDrive
- **Close prompt**: "Chrome and OneDrive need to close for a full clean. Close them now?" → Yes / No / Skip Browser Clean
- **Graceful close**: `WM_CLOSE` message (not `taskkill /F`) — gives user chance to save tabs
- **OneDrive**: Gracefully pause sync → clean → restart sync after
- **Timeout**: Wait for process exit with 10s timeout, warn if still running

#### Clean Results
- Before/after disk space comparison
- Itemised breakdown: "Temp files: 1.2 GB | Browser cache: 340 MB | Recycle Bin: 890 MB"
- Total space reclaimed prominently displayed
- Full operation log with timestamps

---

### 3. Registry Cleaner

#### Safe Registry Cleaning
- **Scan categories**:
  - Missing shared DLLs
  - Unused file extensions
  - Orphaned COM/ActiveX entries
  - Invalid application paths
  - Obsolete software entries (uninstalled program leftovers)
  - Missing MUI references
  - Invalid firewall rules
  - Stale installer references
  - Dead shortcut references
- **Safety measures**:
  - Automatic `.reg` backup before ANY changes (saved to `%APPDATA%\COR Cleanup\Backups\`)
  - Preview list of all entries with human-readable descriptions
  - Select/deselect individual entries
  - **"Fix Selected"** as primary action (not "Fix All")
  - Undo/restore from backup at any time
- **Risk levels**: Colour-coded — green (safe), amber (review), red (caution)
- **Backup management**: View previous backups with dates, restore any, delete old ones

---

### 4. Uninstaller

#### Standard Uninstall + Deep Clean (Revo Uninstaller style)
- **Software list**: All installed programs from registry (NOT Win32_Product — that's slow and triggers MSI reconfiguration)
  - Name, publisher, version, install date, estimated size
  - Sort by any column, search/filter
  - Detect Windows Apps (UWP/Store) separately
- **Uninstall workflow**:
  1. Run the program's own uninstaller
  2. After uninstaller completes, deep scan for leftovers:
     - Registry entries (orphaned keys from the program)
     - Files and folders (leftover directories in Program Files, AppData, ProgramData)
  3. Show leftovers with "safe/review/caution" ratings
  4. User selects what to remove → deleted to Recycle Bin (recoverable)
- **Forced uninstall**: For programs not listed or partially removed — point to install folder, tool identifies and removes
- **Batch uninstall**: Select multiple programs, uninstall in sequence
- **Windows Apps**: Separate tab for UWP/Store apps — remove bloatware

---

### 5. Hardware Info & Health

#### RAM Identification & Upgrade Advisor
- **Per-DIMM breakdown** (via WMI `Win32_PhysicalMemory`):
  - Slot label (DIMM1, DIMM2, ChannelA-DIMM0, etc.)
  - Capacity per stick (e.g., 8 GB)
  - Speed (MHz)
  - Type (DDR4/DDR5) — decoded from SMBIOSMemoryType
  - Manufacturer (Samsung, Kingston, Crucial, SK Hynix)
  - Part number and serial
  - Form factor (DIMM/SO-DIMM)
  - ECC detection (TotalWidth 72-bit = ECC)
- **Slot summary**: "2 of 4 slots used" — instantly shows if upgrade possible
- **Max capacity**: From `Win32_PhysicalMemoryArray.MaxCapacity`
- **Upgrade recommendation**: "You have 16 GB in 2 slots. 2 empty slots available. Max supported: 64 GB. You can add 2x 16 GB DDR4-3200 sticks."
- **Dual/single channel**: Detect memory channel configuration

#### Disk Health Dashboard (CrystalDiskInfo style)
- **Full S.M.A.R.T. attribute table**: Current value, worst recorded, threshold, raw counter
- **Colour-coded health**: Blue (Good), Yellow (Caution), Red (Bad)
- **Key metrics highlighted**:
  - Reallocated sectors count
  - Pending sectors
  - Power-on hours
  - Temperature (real-time)
  - Read error rate
  - SSD: Wear levelling count, NVMe wear percentage
- **Multi-drive**: Monitor all drives simultaneously (SATA, NVMe, USB external)
- **Alert thresholds**: Flag drives approaching failure

#### System Info Dashboard
- **OS**: Edition (Pro/Home), version, build, install date, product key (recovered from BIOS/registry)
- **CPU**: Model, codename, cores/threads, base/boost clock, cache hierarchy (L1/L2/L3)
- **GPU**: Model, driver version, VRAM
- **Motherboard**: Manufacturer, model, BIOS version/date, chipset
- **Network**: Adapters, IPs, MACs, connection type, speed
- **Peripherals**: Connected USB devices

#### Battery Health (laptops)
- Design capacity vs current full charge capacity
- **Health percentage**: (current / design) x 100
- Cycle count
- Degradation indicator — flag replacement at <80%
- Chemistry type, manufacturer
- Charge/discharge rate

#### Windows Product Key Recovery
- BIOS/UEFI embedded OEM key (`SoftwareLicensingService.OA3xOriginalProductKey`)
- Registry-stored retail key (decoded from `DigitalProductId`)
- Copy to clipboard with one click
- Licence activation status

#### Wi-Fi Password Recovery
- List all saved SSIDs from `netsh wlan show profiles`
- Show stored password for each (`key=clear`)
- Copy individual passwords to clipboard
- Export all to text file
- Requires admin privileges

---

### 6. Tools & Utilities

#### Disk Space Analyser (TreeSize/WinDirStat style)
- Interactive treemap or bar chart of disk usage by folder
- Drill-down navigation — click into folders
- Top 50 largest files list
- File type breakdown (documents, images, videos, archives, etc.)
- Age distribution — find old files taking space

#### Duplicate File Finder
- Multi-stage algorithm for speed:
  1. Group by file size (eliminates ~95% instantly)
  2. Hash first 4 KB of matching-size files
  3. Full SHA-256 hash only for remaining matches
- Filter by type, size, location
- Preview before delete
- Keep newest/oldest/specific option
- Delete to Recycle Bin (recoverable)

#### File Shredder / Secure Delete
- Methods: 1-pass zero fill (fast), DoD 3-pass (standard), 7-pass (enhanced)
- Drag-and-drop files or right-click
- **Free space wiper**: Overwrite free disk space for decommissioned drives — essential for MSP data destruction
- Modern drives: 1-3 passes is sufficient (35-pass Gutmann is obsolete)

#### BSOD Crash History Viewer (BlueScreenView style)
- Parse minidumps from `C:\Windows\Minidump\`
- Table view: Date, bug check code, human-readable name, faulting driver/module
- No WinDbg required — just header parsing
- Instant diagnosis of recurring crashes

#### Software Inventory
- All installed programs (registry-based enumeration, NOT `Win32_Product`)
- Name, version, publisher, install date, estimated size
- Sort by size to find space hogs
- Search and filter
- Export to CSV for audits and licence compliance

#### File Hash Checker
- Drag-and-drop any file → get MD5, SHA1, SHA256 hashes
- Paste expected hash → instant match/mismatch verification
- Verify downloaded installers match vendor-published hashes
- Batch hash multiple files
- Copy any hash to clipboard

#### Password Generator
- Generate secure passwords: configurable length, uppercase/lowercase/numbers/symbols
- Exclude ambiguous characters (0/O, 1/l/I) option
- Passphrase mode: generate memorable word-based passwords
- Copy to clipboard with one click
- Useful for creating client Wi-Fi keys, account passwords during setup

#### Context Menu Manager (v1.1)
- List all right-click menu entries from registry locations
- Enable/disable individual items
- Clean up bloated context menus

#### Broken Shortcut Finder (v1.1)
- Scan Start Menu, Desktop, Taskbar for dead shortcuts
- Auto-fix or delete

---

### 7. System Admin

#### Enhanced Startup Manager (Autoruns-inspired)
- **Sources scanned**:
  - Registry Run/RunOnce keys
  - Startup folder
  - Scheduled Tasks (with suspicious path flagging)
  - Services (auto-start)
  - Shell extensions
  - Browser extensions summary
- Enable/disable toggle per item
- **Signature verification**: Show signed vs unsigned executables
- **"Hide Microsoft entries"** filter — shows third-party only (catches malware/bloatware)
- Impact rating (high/medium/low)
- Publisher and full file path

#### Windows Services Manager
- List all services with status (Running/Stopped), startup type (Auto/Manual/Disabled)
- Start, stop, restart any service
- Change startup type
- **"Safe to disable" guidance**: Known bloatware services flagged (Xbox Game Bar, Cortana, SysMain on SSD, Connected User Experiences, DiagTrack, etc.)
- Group by category: Microsoft core, Microsoft optional, third-party
- Search and filter
- Different from startup manager — services run without a user logged in

#### Event Log Viewer (simplified)
- Pull Critical + Error events from System and Application logs
- Human-readable descriptions (not raw Event IDs)
- Filter by date range, source, severity
- Search across all events
- "Last 24 hours" / "Last 7 days" / "Last 30 days" quick filters
- Common error explanations: map frequent Event IDs to plain English causes
- Export filtered results to CSV/text

#### System Repair Tools
- **SFC (System File Checker)**: GUI wrapper for `sfc /scannow` — progress bar, real-time output, results summary
- **DISM Repair**: GUI for `DISM /Online /Cleanup-Image /RestoreHealth` — progress, results, recommended next steps
- **Network Reset**: Full stack reset — Winsock, TCP/IP, release/renew DHCP, flush DNS, flush ARP cache (the nuclear option for "nothing network works")
- **Windows Update Reset**: Stop update services, clear cache, restart — fixes stuck updates
- Each repair shows: what it does, estimated time, results, what to try next if it fails

#### Printer Management
- List all installed printers with status (Ready/Error/Offline)
- **Clear stuck print queue**: Stop spooler → delete queue files → restart spooler (the #1 printer fix)
- Remove old/ghost printers
- Set default printer
- Restart Print Spooler service
- Show printer IP/port for network printers
- "Printer won't print" = ~20% of MSP calls — this section alone justifies the tool

#### Firewall Rules Viewer
- List all Windows Firewall inbound and outbound rules
- Search by port number, program name, or rule name
- Filter: enabled/disabled, allow/block, inbound/outbound
- "Why can't this app connect?" → find the blocking rule instantly
- Enable/disable individual rules
- Show which profile (Domain/Private/Public) each rule applies to

#### Hosts File Editor
- View/edit `C:\Windows\System32\drivers\etc\hosts` with proper GUI
- Add/remove entries with validation
- Toggle entries on/off (comment/uncomment) without deleting
- **Presets**: Block Windows telemetry, block common ad domains
- Syntax highlighting and duplicate detection
- Backup before changes

#### Environment Variables Editor
- View/edit system and user environment variables
- **PATH editor**: Visual list of PATH entries — add, remove, reorder, detect broken paths
- Common variables (JAVA_HOME, NODE_PATH, etc.) with auto-detection
- Better than the 5-clicks-deep Windows UI

#### Quick Admin Actions
- Flush DNS cache
- Reset network adapter
- Clear Windows Store cache (`wsreset`)
- Rebuild icon cache
- Windows Defender quick scan trigger
- Check for Windows Updates
- System Restore point manager — view, create, delete

#### System Summary Export (COR Intelligence branded)
- One-click HTML report of complete system status:
  - Hardware info (CPU, RAM slots, GPU, disks, battery)
  - Disk health (S.M.A.R.T. summary)
  - Software inventory
  - Network configuration
  - Startup items
  - Windows licence and update status
- COR Intelligence branding and logo
- Attach to support tickets, email to clients
- PDF export option

---

## Architecture

```
CORCleanup/
├── CORCleanup.sln                        # Solution file
├── CORCleanup/                            # Main WPF application project
│   ├── App.xaml / App.xaml.cs             # Application entry point
│   ├── MainWindow.xaml / .cs              # Shell with sidebar navigation
│   ├── app.manifest                       # UAC elevation (requireAdministrator)
│   ├── Views/                             # WPF Views (XAML + code-behind)
│   │   ├── NetworkView.xaml               # Ping, Traceroute, Scanner, DNS, Ports, Speed, Subnet
│   │   ├── CleanupView.xaml               # System + Browser cleanup
│   │   ├── RegistryView.xaml              # Registry cleaner
│   │   ├── UninstallerView.xaml           # Program uninstaller
│   │   ├── HardwareView.xaml              # RAM, Disk health, System info, Battery, Keys, Wi-Fi
│   │   ├── ToolsView.xaml                 # Disk analyser, Duplicates, Shredder, BSOD, Inventory, Hash, Passwords
│   │   └── AdminView.xaml                 # Startup, Services, Event Log, Repair, Printer, Firewall, Hosts, Env Vars
│   ├── ViewModels/                        # MVVM ViewModels (one per View)
│   │   ├── NetworkViewModel.cs
│   │   ├── CleanupViewModel.cs
│   │   ├── RegistryViewModel.cs
│   │   ├── UninstallerViewModel.cs
│   │   ├── HardwareViewModel.cs
│   │   ├── ToolsViewModel.cs
│   │   └── AdminViewModel.cs
│   ├── Services/                          # Business logic (no UI dependency)
│   │   ├── Network/
│   │   │   ├── PingService.cs             # Continuous ping + traceroute
│   │   │   ├── NetworkScannerService.cs   # ARP/ICMP subnet scanner
│   │   │   ├── DnsLookupService.cs        # DNS record queries (A, MX, CNAME, TXT, NS)
│   │   │   ├── PortScannerService.cs      # Remote port scan + local listening ports
│   │   │   ├── ConnectionMonitorService.cs # Active connections (TCPView equivalent)
│   │   │   ├── SpeedTestService.cs        # Bandwidth test
│   │   │   └── SubnetCalculatorService.cs # IP/CIDR calculations
│   │   ├── Cleanup/
│   │   │   ├── SystemCleanupService.cs    # Temp files, Windows junk
│   │   │   ├── BrowserCleanupService.cs   # Browser cache/data per browser
│   │   │   └── AppCleanupService.cs       # Application-specific temp files
│   │   ├── RegistryService.cs             # Registry scan, clean, backup/restore
│   │   ├── UninstallService.cs            # Uninstall + leftover scan + forced uninstall
│   │   ├── Hardware/
│   │   │   ├── WmiService.cs              # All WMI queries (RAM, drivers, SMART, battery, CPU, GPU, mobo)
│   │   │   ├── SmartService.cs            # S.M.A.R.T. disk health monitoring
│   │   │   ├── BatteryService.cs          # Battery health + degradation tracking
│   │   │   └── DriverService.cs           # Driver enumeration and info
│   │   ├── Admin/
│   │   │   ├── StartupService.cs          # Startup/autorun item management
│   │   │   ├── ServicesService.cs         # Windows services management
│   │   │   ├── EventLogService.cs         # Simplified event log reader
│   │   │   ├── SystemRepairService.cs     # SFC, DISM, network reset, update reset
│   │   │   ├── PrinterService.cs          # Printer management, queue clearing
│   │   │   ├── FirewallService.cs         # Firewall rules viewer
│   │   │   ├── HostsFileService.cs        # Hosts file editor
│   │   │   └── EnvironmentService.cs      # Environment variables editor
│   │   ├── Tools/
│   │   │   ├── DiskAnalyserService.cs     # Treemap data, folder sizes
│   │   │   ├── DuplicateFinderService.cs  # Multi-stage hash duplicate detection
│   │   │   ├── FileShredderService.cs     # Secure delete + free space wipe
│   │   │   ├── HashCheckerService.cs      # MD5/SHA1/SHA256 file hashing
│   │   │   └── PasswordGeneratorService.cs # Secure password generation
│   │   ├── CrashDumpService.cs            # Minidump parser (BSOD viewer)
│   │   ├── WifiService.cs                 # Saved Wi-Fi password recovery
│   │   ├── ProductKeyService.cs           # Windows licence key recovery
│   │   ├── ProcessService.cs              # Graceful process close (browsers, OneDrive)
│   │   ├── SoftwareInventoryService.cs    # Installed programs enumeration
│   │   └── ReportService.cs               # HTML/PDF system report generator
│   ├── Models/                            # Data models (POCOs for each service)
│   ├── Controls/                          # Reusable WPF controls
│   │   ├── DataTableControl.xaml          # Sortable/filterable/exportable table
│   │   ├── LatencyChart.xaml              # Real-time ping/traceroute graph
│   │   ├── TreemapControl.xaml            # Disk space visualisation
│   │   ├── ProgressPanel.xaml             # Progress bar with status text
│   │   ├── ToggleSwitch.xaml              # On/off toggle for services, startup items
│   │   └── ConfirmDialog.xaml             # Confirmation prompts
│   ├── Converters/                        # WPF value converters
│   ├── Themes/                            # Dark/light theme resources (Fluent)
│   ├── Assets/                            # Icons, COR logo, branding, OUI database
│   │   └── oui.csv                        # MAC vendor lookup (bundled, no network needed)
│   └── Scripts/                           # PowerShell scripts called from C#
│       ├── Invoke-Cleanup.ps1
│       ├── Get-BrowserData.ps1
│       ├── Scan-Registry.ps1
│       ├── Get-WifiPasswords.ps1
│       ├── Get-NetworkScan.ps1
│       ├── Invoke-SystemRepair.ps1        # SFC + DISM wrapper
│       ├── Reset-NetworkStack.ps1         # Full Winsock/TCP/IP/DHCP/DNS/ARP reset
│       └── Reset-WindowsUpdate.ps1        # Stop services, clear cache, restart
├── CORCleanup.Core/                       # Shared library (interfaces, models, helpers)
├── CORCleanup.Tests/                      # Unit + integration tests
└── .github/
    └── workflows/
        └── build-windows.yml              # CI/CD: Build + test + installer on windows-latest
```

## Windows-Specific Rules

### UAC Elevation
```xml
<!-- app.manifest — one line, built-in .NET -->
<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
```
- Most features require admin (cleanup, registry, services, network scan, drivers)
- System info dashboard degrades gracefully without admin

### WMI Queries (the backbone for hardware info)
```csharp
// RAM DIMM info — this simple in .NET
var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
foreach (var obj in searcher.Get()) {
    // Slot, Capacity, Speed, Type, Manufacturer, PartNumber — all available
}

// S.M.A.R.T. disk health
var disks = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");

// Drivers
var drivers = new ManagementObjectSearcher("SELECT * FROM Win32_PnPSignedDriver");

// Battery
var battery = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
```

### PowerShell Execution
- PowerShell 5.1 (ships with Win11) for operations where it's simpler than pure C#
- `-ExecutionPolicy Bypass -File` for our bundled scripts only
- All scripts handle Pro and Home editions

### Registry Safety
- Automatic `.reg` backup before any modification
- Backups at `%APPDATA%\COR Cleanup\Backups\` (user-accessible)
- All changes logged with timestamps for rollback
- Confirmation required before every modification

### Win11 Pro vs Home Detection
```
Registry: HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\EditionID
"Professional" = Pro, "Core" = Home
```
Pro-only features (greyed out on Home with tooltip explaining):
- BitLocker status
- Group Policy references
- Hyper-V status
- Remote Desktop configuration

### Process Management
- Detect running browsers and OneDrive before cleanup
- `WM_CLOSE` (graceful) not `taskkill /F` (force-kill)
- 10-second timeout, then warn if still running
- Restart OneDrive sync after cleanup

## UI/UX Guidelines

- **Theme**: Dark mode primary (Windows 11 Fluent), light mode toggle
- **Branding**: COR Intelligence colours, logo, "COR Cleanup" wordmark
- **Layout**: Sidebar with 7 sections — Network, Cleanup, Registry, Uninstaller, Hardware, Tools, Admin
- **Active indicators**: Sidebar badges for running operations (ping active, scan in progress)
- **Progress**: Real-time progress bars for cleanup, scan, and analysis operations
- **Results**: Before/after metrics with prominent totals
- **Safety**: Confirmation dialogs before destructive operations, preview mode for registry
- **Tables**: Consistent sortable/filterable/exportable DataTable control across all views
- **Accessibility**: Keyboard navigation, screen reader support, WCAG 2.1 AA
- **Portable mode**: Detect if running from USB drive, store all data alongside .exe

## Development Rules

### Build & Test
- Develop on macOS, build via GitHub Actions `windows-latest` runner
- Test on Windows 11 VMs (Pro and Home)
- MVVM pattern throughout — Views (XAML) + ViewModels (C#) + Services (business logic)
- Unit tests for all Services, integration tests for WMI/registry operations

### Safety
- Whitelist approach: only delete from known-safe locations
- Never delete system-critical files or user documents
- Dry-run/preview mode for all destructive operations
- Comprehensive logging to `%APPDATA%\COR Cleanup\Logs\`
- All cleanup operations cancellable mid-execution
- Registry always backed up before modification
- Uninstall leftovers deleted to Recycle Bin (recoverable)

### Code Standards
- C#: .NET 8 target, nullable reference types enabled, follow Microsoft coding conventions
- XAML: Consistent naming, resource dictionaries for themes
- PowerShell: approved verbs, comment-based help, try/catch
- UK English in all user-facing strings

## Security

- No telemetry or data collection
- No network calls except: ping targets (user-initiated), network scanner (local subnet), public IP check (optional), MAC OUI lookup (local database bundled, not online)
- All operations local to the machine
- Code signing for installer (COR Intelligence certificate)
- No bundled third-party engines — all our own code
- No driver downloads — information display only
- Wi-Fi passwords and product keys shown only to admin user (already elevated)

## Release Versions

### v1.0 — Core Release
All 7 sections as described above — the "replaces 7 tools" milestone.

### v1.1 — Enhancement Release
- Wake-on-LAN from scanner
- Network share discovery (SMB)
- RDP quick-connect from scanner
- Context menu manager
- Broken shortcut finder
- Browser extension manager (single pane across browsers)
- SQLite vacuum for browser performance
- Empty folder finder
- Enhanced scheduled task auditor (suspicious path flagging)

### v1.2 — Enterprise Features
- Remote shutdown/restart from scanner
- Driver backup/restore
- Software update checker
- Simple CPU benchmark
- System Restore point management
- Scheduled cleanup tasks (Windows Task Scheduler)

## Git & Deployment

- **Repo**: WebSmartTeam GitHub org
- **Branch strategy**: `main` for releases, feature branches for development
- **Versioning**: SemVer (v1.0.0 format)
- **Releases**: GitHub Releases with signed `.exe` installer + portable `.zip`
- **CI/CD**: GitHub Actions on `windows-latest` — build, test, create installer, create release

using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CORCleanup.ViewModels;

public sealed class ToolInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
}

public sealed class ToolSection
{
    public required string Name { get; init; }
    public required string IconSymbol { get; init; }
    public required List<ToolInfo> Tools { get; init; }
}

public partial class HelpViewModel : ObservableObject
{
    private readonly List<ToolSection> _allSections;

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private ObservableCollection<ToolSection> _filteredSections = [];

    public HelpViewModel()
    {
        _allSections = BuildToolData();
        FilteredSections = new ObservableCollection<ToolSection>(_allSections);
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            FilteredSections = new ObservableCollection<ToolSection>(_allSections);
            return;
        }

        var query = value.Trim();
        var filtered = new List<ToolSection>();

        foreach (var section in _allSections)
        {
            var matchingTools = section.Tools
                .Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || t.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchingTools.Count > 0)
            {
                filtered.Add(new ToolSection
                {
                    Name = section.Name,
                    IconSymbol = section.IconSymbol,
                    Tools = matchingTools
                });
            }
        }

        FilteredSections = new ObservableCollection<ToolSection>(filtered);
    }

    [RelayCommand]
    private void OpenIdeaPortal()
    {
        Process.Start(new ProcessStartInfo("https://corintelligence.co.uk/ideasportal")
        {
            UseShellExecute = true
        });
    }

    private static List<ToolSection> BuildToolData() =>
    [
        new()
        {
            Name = "Auto Tool",
            IconSymbol = "Wand24",
            Tools =
            [
                new() { Name = "One-Click Scan", Description = "Automatically scans your system for issues across all categories — cleanup, registry, startup items, disk health, and drivers." },
                new() { Name = "Auto Fix", Description = "Applies recommended fixes for all detected issues in a single operation with full backup and rollback capability." },
            ]
        },
        new()
        {
            Name = "Dashboard",
            IconSymbol = "Home24",
            Tools =
            [
                new() { Name = "System Overview", Description = "At-a-glance view of your PC — OS, CPU, GPU, memory usage, storage capacity, network status, and battery health." },
                new() { Name = "Quick Actions", Description = "One-click shortcuts to common tasks like cleanup, network diagnostics, and system repair." },
                new() { Name = "Health Indicators", Description = "Colour-coded status badges showing disk health, driver status, startup impact, and available updates." },
            ]
        },
        new()
        {
            Name = "Network & Diagnostics",
            IconSymbol = "Globe24",
            Tools =
            [
                new() { Name = "Continuous Ping", Description = "Ping any host continuously (like ping -t) with live latency graph, min/max/avg stats, and packet loss tracking." },
                new() { Name = "Traceroute", Description = "Visual hop-by-hop path to any target, highlighting slow hops and timeouts with latency per hop." },
                new() { Name = "Network Scanner", Description = "Scan your local subnet to discover all devices — IP, hostname, MAC address, vendor, and shared folders." },
                new() { Name = "DNS Lookup", Description = "Query A, AAAA, MX, CNAME, TXT, NS, and SOA records using Google, Cloudflare, or custom DNS servers." },
                new() { Name = "Port Scanner", Description = "Check if specific ports are open on remote hosts, or see what's listening locally with process identification." },
                new() { Name = "Active Connections", Description = "Real-time view of all TCP/UDP connections — process, local/remote address, state, and bandwidth usage." },
                new() { Name = "Speed Test", Description = "Test your internet bandwidth — download, upload, latency, and jitter without opening a browser." },
                new() { Name = "Subnet Calculator", Description = "Calculate network ranges, broadcast addresses, and usable hosts from IP/CIDR notation." },
                new() { Name = "Wi-Fi Info", Description = "View connected network details, signal strength, nearby networks, and saved Wi-Fi passwords." },
                new() { Name = "Quick Network Info", Description = "Active adapter, IP address, gateway, DNS servers, public IP, and connection type at a glance." },
            ]
        },
        new()
        {
            Name = "System Cleanup",
            IconSymbol = "Broom24",
            Tools =
            [
                new() { Name = "Windows Cleanup", Description = "Remove temp files, Windows Update cache, error reports, prefetch, thumbnail cache, and other system junk." },
                new() { Name = "Browser Cleanup", Description = "Clear browser cache and download history for Chrome, Edge, Firefox, and Brave. Preserves passwords, history, and cookies by default." },
                new() { Name = "Application Cleanup", Description = "Remove temporary files from Adobe, Microsoft Office, Java, and other installed applications." },
                new() { Name = "Pre-Clean Detection", Description = "Automatically detects running browsers and OneDrive, offering to close them gracefully before cleaning." },
            ]
        },
        new()
        {
            Name = "Uninstaller",
            IconSymbol = "AppsList24",
            Tools =
            [
                new() { Name = "Program Uninstaller", Description = "Uninstall any program and then deep-scan for leftover files and registry entries that the standard uninstaller missed." },
                new() { Name = "Windows Apps", Description = "Remove pre-installed Windows Store apps and bloatware that can't be uninstalled through normal Settings." },
                new() { Name = "Forced Uninstall", Description = "Remove programs that don't appear in the standard list or were only partially uninstalled." },
                new() { Name = "Batch Uninstall", Description = "Select multiple programs and uninstall them in sequence — ideal for cleaning up new machines." },
            ]
        },
        new()
        {
            Name = "System Info",
            IconSymbol = "Desktop24",
            Tools =
            [
                new() { Name = "COR Spec", Description = "Complete system specification in one view — OS, CPU, RAM, GPU, storage, audio, network, and battery. Export as branded HTML or copy to clipboard." },
                new() { Name = "RAM Details", Description = "Per-DIMM breakdown — slot, capacity, speed, type, manufacturer, and part number. Shows upgrade recommendations." },
                new() { Name = "Disk Health", Description = "S.M.A.R.T. monitoring for all drives — health status, temperature, power-on hours, reallocated sectors, and wear level." },
                new() { Name = "Battery Health", Description = "Design vs current capacity, health percentage, cycle count, and replacement recommendation for laptops." },
                new() { Name = "Product Key Recovery", Description = "Recover your Windows product key from BIOS/UEFI or registry. Copy to clipboard with one click." },
                new() { Name = "Wi-Fi Passwords", Description = "View saved passwords for all Wi-Fi networks this PC has connected to. Export all to text file." },
                new() { Name = "Outdated Drivers", Description = "List drivers older than 3 years that may need updating, with manufacturer and version details." },
            ]
        },
        new()
        {
            Name = "Disk Tools",
            IconSymbol = "Wrench24",
            Tools =
            [
                new() { Name = "Disk Space Analyser", Description = "Interactive treemap showing disk usage by folder. Drill down to find what's consuming your storage." },
                new() { Name = "Duplicate File Finder", Description = "Find duplicate files using a multi-stage hash algorithm. Preview before deleting, with keep-newest/oldest options." },
                new() { Name = "File Shredder", Description = "Securely delete files beyond recovery — 1-pass zero fill, DoD 3-pass, or 7-pass overwrite methods." },
                new() { Name = "BSOD Crash Viewer", Description = "Parse Windows minidump files to show crash history — date, bug check code, and faulting driver without needing WinDbg." },
                new() { Name = "Software Inventory", Description = "Complete list of all installed software with version, publisher, and install date. Export to CSV for audits." },
                new() { Name = "File Hash Checker", Description = "Generate MD5, SHA1, and SHA256 hashes for any file. Paste an expected hash to verify integrity." },
                new() { Name = "Password Generator", Description = "Generate secure random passwords with configurable length, character types, and ambiguous character exclusion." },
                new() { Name = "Process Explorer", Description = "View running processes with CPU, memory, and thread details. Identify resource-hungry applications." },
                new() { Name = "Antivirus Status", Description = "Check which antivirus products are installed, their status, and whether real-time protection is active." },
            ]
        },
        new()
        {
            Name = "System Admin",
            IconSymbol = "ShieldTask24",
            Tools =
            [
                new() { Name = "Startup Manager", Description = "View and control all auto-start programs — registry, startup folder, scheduled tasks, and services. Toggle items on/off." },
                new() { Name = "Services Manager", Description = "Manage Windows services — start, stop, restart, and change startup type. Flags known-safe-to-disable bloatware services." },
                new() { Name = "Event Log Viewer", Description = "View Critical and Error events from System and Application logs with human-readable descriptions and date filtering." },
                new() { Name = "System Repair", Description = "GUI wrappers for SFC, DISM, network stack reset, and Windows Update reset — with progress bars and results." },
                new() { Name = "Printer Management", Description = "Clear stuck print queues, remove ghost printers, restart the Print Spooler, and set default printer." },
                new() { Name = "Firewall Rules", Description = "Browse all Windows Firewall rules — search by port, program, or rule name. Enable/disable individual rules." },
                new() { Name = "Hosts File Editor", Description = "View and edit the Windows hosts file with a proper GUI. Toggle entries on/off, with telemetry-blocking presets." },
                new() { Name = "Environment Variables", Description = "Edit system and user environment variables with a visual PATH editor — add, remove, reorder, and detect broken paths." },
                new() { Name = "Windows Debloater", Description = "Remove unnecessary Windows features, telemetry services, and pre-installed apps to streamline your system." },
                new() { Name = "System Report", Description = "Generate a comprehensive COR Intelligence-branded HTML report of your complete system status for support tickets." },
            ]
        },
    ];
}

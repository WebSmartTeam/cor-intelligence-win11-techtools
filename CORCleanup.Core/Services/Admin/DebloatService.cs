using System.Diagnostics;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Core.Security;

namespace CORCleanup.Core.Services.Admin;

/// <summary>
/// Manages Windows 11 bloatware removal, Copilot/Recall disabling, and privacy tweaks.
/// Uses Windows PowerShell 5.1 (not pwsh 7) for AppX cmdlet compatibility.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DebloatService : IDebloatService
{
    /// <summary>
    /// Known Win11 bloatware packages with MSP-friendly safety ratings.
    /// Excludes system-critical packages (Store, DesktopAppInstaller, VCLibs, etc.).
    /// </summary>
    private static readonly AppxPackageInfo[] KnownBloatware =
    {
        // AI / Copilot
        new() { PackageName = "Microsoft.Copilot", FriendlyName = "Microsoft Copilot", Category = DebloatCategory.AiCopilot, Safety = DebloatSafety.Safe, Description = "Standalone Copilot app. Windows Update may reinstall it." },
        new() { PackageName = "Microsoft.Windows.Copilot", FriendlyName = "Windows Copilot (integrated)", Category = DebloatCategory.AiCopilot, Safety = DebloatSafety.Safe, Description = "Integrated Copilot experience on 24H2+ builds." },
        new() { PackageName = "Microsoft.549981C3F5F10", FriendlyName = "Cortana", Category = DebloatCategory.AiCopilot, Safety = DebloatSafety.Safe, Description = "Cortana is end-of-life. Safe to remove." },

        // Xbox / Gaming
        new() { PackageName = "Microsoft.GamingApp", FriendlyName = "Xbox App", Category = DebloatCategory.Xbox, Safety = DebloatSafety.Review, Description = "Required for PC Game Pass. Safe if client does not game." },
        new() { PackageName = "Microsoft.XboxGameOverlay", FriendlyName = "Xbox Game Overlay", Category = DebloatCategory.Xbox, Safety = DebloatSafety.Review, Description = "In-game overlay. Some games expect it." },
        new() { PackageName = "Microsoft.XboxGamingOverlay", FriendlyName = "Xbox Game Bar", Category = DebloatCategory.Xbox, Safety = DebloatSafety.Review, Description = "Performance overlay and screen recording." },
        new() { PackageName = "Microsoft.XboxIdentityProvider", FriendlyName = "Xbox Identity Provider", Category = DebloatCategory.Xbox, Safety = DebloatSafety.Caution, Description = "Xbox Live authentication. Removing may break Store game purchases." },
        new() { PackageName = "Microsoft.XboxSpeechToTextOverlay", FriendlyName = "Xbox Speech to Text", Category = DebloatCategory.Xbox, Safety = DebloatSafety.Safe, Description = "Accessibility speech-to-text in games." },
        new() { PackageName = "Microsoft.Xbox.TCUI", FriendlyName = "Xbox TCUI", Category = DebloatCategory.Xbox, Safety = DebloatSafety.Review, Description = "Xbox social/text chat UI for multiplayer." },
        new() { PackageName = "Microsoft.XboxApp", FriendlyName = "Xbox Console Companion", Category = DebloatCategory.Xbox, Safety = DebloatSafety.Safe, Description = "Legacy Xbox app. No longer supported." },

        // Entertainment
        new() { PackageName = "Microsoft.ZuneMusic", FriendlyName = "Media Player", Category = DebloatCategory.Entertainment, Safety = DebloatSafety.Review, Description = "Default music player. Removing leaves no default media player." },
        new() { PackageName = "Microsoft.ZuneVideo", FriendlyName = "Films and TV", Category = DebloatCategory.Entertainment, Safety = DebloatSafety.Safe, Description = "Video store/player. Rarely used." },
        new() { PackageName = "Microsoft.MicrosoftSolitaireCollection", FriendlyName = "Solitaire Collection", Category = DebloatCategory.Entertainment, Safety = DebloatSafety.Safe, Description = "Card games with advertisements." },
        new() { PackageName = "Clipchamp.Clipchamp", FriendlyName = "Clipchamp Video Editor", Category = DebloatCategory.Entertainment, Safety = DebloatSafety.Safe, Description = "Microsoft-owned video editor." },
        new() { PackageName = "Microsoft.BingNews", FriendlyName = "Microsoft News", Category = DebloatCategory.Entertainment, Safety = DebloatSafety.Safe, Description = "News aggregator via Bing." },
        new() { PackageName = "Microsoft.BingWeather", FriendlyName = "Weather", Category = DebloatCategory.Entertainment, Safety = DebloatSafety.Safe, Description = "Weather app. Widgets can show weather without this." },

        // Communication
        new() { PackageName = "Microsoft.OutlookForWindows", FriendlyName = "New Outlook", Category = DebloatCategory.Communication, Safety = DebloatSafety.Review, Description = "Replacing Mail and Calendar. Some organisations require it." },
        new() { PackageName = "microsoft.windowscommunicationsapps", FriendlyName = "Mail and Calendar", Category = DebloatCategory.Communication, Safety = DebloatSafety.Safe, Description = "Legacy apps being deprecated for new Outlook." },
        new() { PackageName = "MSTeams", FriendlyName = "Teams (personal)", Category = DebloatCategory.Communication, Safety = DebloatSafety.Safe, Description = "Consumer Teams. Enterprise Teams is a separate install." },
        new() { PackageName = "Microsoft.SkypeApp", FriendlyName = "Skype", Category = DebloatCategory.Communication, Safety = DebloatSafety.Safe, Description = "Legacy. Microsoft discontinued consumer Skype." },
        new() { PackageName = "Microsoft.People", FriendlyName = "People", Category = DebloatCategory.Communication, Safety = DebloatSafety.Safe, Description = "Contact manager. Rarely used standalone." },
        new() { PackageName = "Microsoft.YourPhone", FriendlyName = "Phone Link", Category = DebloatCategory.Communication, Safety = DebloatSafety.Review, Description = "Android/iPhone integration. Some users rely on it." },

        // Productivity
        new() { PackageName = "Microsoft.MicrosoftOfficeHub", FriendlyName = "Office Hub", Category = DebloatCategory.Productivity, Safety = DebloatSafety.Safe, Description = "Promotional hub. Not needed if Office is installed." },
        new() { PackageName = "Microsoft.Todos", FriendlyName = "Microsoft To Do", Category = DebloatCategory.Productivity, Safety = DebloatSafety.Safe, Description = "Task manager app." },
        new() { PackageName = "Microsoft.PowerAutomateDesktop", FriendlyName = "Power Automate Desktop", Category = DebloatCategory.Productivity, Safety = DebloatSafety.Safe, Description = "RPA tool. Most users never use it." },
        new() { PackageName = "Microsoft.MicrosoftStickyNotes", FriendlyName = "Sticky Notes", Category = DebloatCategory.Productivity, Safety = DebloatSafety.Review, Description = "Some users actively use this. Check before removing." },
        new() { PackageName = "MicrosoftCorporationII.QuickAssist", FriendlyName = "Quick Assist", Category = DebloatCategory.Productivity, Safety = DebloatSafety.Review, Description = "Remote assistance. MSPs may use alternatives." },
        new() { PackageName = "Microsoft.Getstarted", FriendlyName = "Tips / Get Started", Category = DebloatCategory.Productivity, Safety = DebloatSafety.Safe, Description = "Windows tips and tutorials." },
        new() { PackageName = "Microsoft.GetHelp", FriendlyName = "Get Help", Category = DebloatCategory.Productivity, Safety = DebloatSafety.Safe, Description = "Links to Microsoft support." },

        // System Extras
        new() { PackageName = "Microsoft.WindowsFeedbackHub", FriendlyName = "Feedback Hub", Category = DebloatCategory.SystemExtras, Safety = DebloatSafety.Safe, Description = "Bug reporting to Microsoft. No end-user need." },
        new() { PackageName = "Microsoft.WindowsMaps", FriendlyName = "Windows Maps", Category = DebloatCategory.SystemExtras, Safety = DebloatSafety.Safe, Description = "Mapping app." },
        new() { PackageName = "Microsoft.WindowsSoundRecorder", FriendlyName = "Sound Recorder", Category = DebloatCategory.SystemExtras, Safety = DebloatSafety.Safe, Description = "Basic audio recorder." },
        new() { PackageName = "Microsoft.BingSearch", FriendlyName = "Bing Search", Category = DebloatCategory.SystemExtras, Safety = DebloatSafety.Review, Description = "Bing integration in Start menu search." },
        new() { PackageName = "MicrosoftWindows.Client.WebExperience", FriendlyName = "Widgets", Category = DebloatCategory.SystemExtras, Safety = DebloatSafety.Review, Description = "Powers Widgets panel. Removing breaks Widgets." },
    };

    public Task<List<AppxPackageInfo>> GetBloatwareListAsync() => Task.Run(() =>
    {
        // Get installed packages via PowerShell
        var installed = GetInstalledPackageNames();

        var results = new List<AppxPackageInfo>(KnownBloatware.Length);
        foreach (var template in KnownBloatware)
        {
            var pkg = new AppxPackageInfo
            {
                PackageName = template.PackageName,
                FriendlyName = template.FriendlyName,
                Category = template.Category,
                Safety = template.Safety,
                Description = template.Description,
                IsInstalled = installed.TryGetValue(template.PackageName, out var fullName),
                PackageFullName = fullName,
                IsSelected = template.Safety == DebloatSafety.Safe && installed.ContainsKey(template.PackageName)
            };
            results.Add(pkg);
        }

        return results;
    });

    public async Task<DebloatResult> RemovePackagesAsync(
        IEnumerable<AppxPackageInfo> packages,
        IProgress<string>? progress = null)
    {
        var selected = packages.Where(p => p.IsSelected && p.IsInstalled).ToList();
        int removed = 0, failed = 0, notFound = 0;
        var errors = new List<string>();
        var removedNames = new List<string>();

        foreach (var pkg in selected)
        {
            progress?.Report($"Removing {pkg.FriendlyName}...");

            // Escape package name for PowerShell single-quoted strings (defense-in-depth:
            // values are from static KnownBloatware array, but escaping prevents injection
            // if dynamic values are ever introduced)
            var safeName = InputSanitiser.EscapeForPowerShell(pkg.PackageName);

            // Remove for all users
            var removeResult = await RunPowerShellAsync(
                $"Get-AppxPackage -AllUsers -Name '{safeName}' | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue");

            // Deprovision to prevent reinstallation on new user profiles.
            // Use -eq exact match instead of -like wildcard to prevent wildcard injection.
            await RunPowerShellAsync(
                $"Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -eq '{safeName}' }} | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue");

            if (removeResult == 0)
            {
                removed++;
                removedNames.Add(pkg.FriendlyName);
                progress?.Report($"  Removed {pkg.FriendlyName}");
            }
            else
            {
                // Check if package was already gone
                var checkResult = await RunPowerShellAsync(
                    $"if (Get-AppxPackage -AllUsers -Name '{safeName}') {{ exit 1 }} else {{ exit 0 }}");

                if (checkResult == 0)
                {
                    // Package is gone — treat as success (may have been removed by deprovisioning)
                    removed++;
                    removedNames.Add(pkg.FriendlyName);
                    progress?.Report($"  Removed {pkg.FriendlyName}");
                }
                else
                {
                    failed++;
                    errors.Add($"Failed to remove {pkg.FriendlyName}");
                    progress?.Report($"  Failed: {pkg.FriendlyName}");
                }
            }
        }

        return new DebloatResult
        {
            TotalSelected = selected.Count,
            Removed = removed,
            Failed = failed,
            NotFound = notFound,
            Errors = errors,
            RemovedPackages = removedNames
        };
    }

    public async Task<bool> DisableCopilotAsync(IProgress<string>? progress = null)
    {
        var allOk = true;

        // 1. Remove Copilot AppX packages
        progress?.Report("Removing Copilot AppX packages...");
        await RunPowerShellAsync(
            "Get-AppxPackage -AllUsers -Name 'Microsoft.Copilot' | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue");
        await RunPowerShellAsync(
            "Get-AppxPackage -AllUsers -Name 'Microsoft.Windows.Copilot' | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue");

        // Deprovision both Copilot packages to prevent reinstallation.
        // Use -eq exact match instead of -like wildcard to prevent unintended matches.
        await RunPowerShellAsync(
            "Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq 'Microsoft.Copilot' -or $_.DisplayName -eq 'Microsoft.Windows.Copilot' } | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue");

        // 2. Registry policy — disable Windows Copilot (machine-level)
        progress?.Report("Applying Copilot registry policies...");
        var reg1 = await RunProcessAsync("reg",
            @"add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot"" /v TurnOffWindowsCopilot /t REG_DWORD /d 1 /f");
        if (reg1 != 0) allOk = false;

        // User-level policy
        var reg2 = await RunProcessAsync("reg",
            @"add ""HKCU\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot"" /v TurnOffWindowsCopilot /t REG_DWORD /d 1 /f");
        if (reg2 != 0) allOk = false;

        // 3. Disable Edge Copilot sidebar
        progress?.Report("Disabling Edge Copilot sidebar...");
        await RunProcessAsync("reg",
            @"add ""HKLM\SOFTWARE\Policies\Microsoft\Edge"" /v HubsSidebarEnabled /t REG_DWORD /d 0 /f");
        await RunProcessAsync("reg",
            @"add ""HKLM\SOFTWARE\Policies\Microsoft\Edge"" /v Microsoft365CopilotChatIconEnabled /t REG_DWORD /d 0 /f");

        // 4. Mark as deprovisioned to block Windows Update reinstallation
        progress?.Report("Deprovisioning Copilot to prevent reinstallation...");
        await RunPowerShellAsync(
            @"$pfn = (Get-AppxPackage -AllUsers -Name 'Microsoft.Copilot' -ErrorAction SilentlyContinue).PackageFamilyName; " +
            @"if ($pfn) { reg add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Deprovisioned\$pfn"" /f 2>$null }");

        progress?.Report(allOk ? "Copilot disabled successfully" : "Copilot partially disabled — some policies may not apply on Home edition");
        return allOk;
    }

    public async Task<bool> DisableRecallAsync(IProgress<string>? progress = null)
    {
        var allOk = true;

        progress?.Report("Disabling Windows Recall...");

        // Disable Recall enablement
        var reg1 = await RunProcessAsync("reg",
            @"add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI"" /v AllowRecallEnablement /t REG_DWORD /d 0 /f");
        if (reg1 != 0) allOk = false;

        // Disable AI data analysis (snapshots)
        var reg2 = await RunProcessAsync("reg",
            @"add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI"" /v DisableAIDataAnalysis /t REG_DWORD /d 1 /f");
        if (reg2 != 0) allOk = false;

        progress?.Report(allOk ? "Windows Recall disabled" : "Recall disable partially failed — may require admin privileges");
        return allOk;
    }

    public async Task<bool> ApplyPrivacyTweaksAsync(IProgress<string>? progress = null)
    {
        var allOk = true;
        var commands = new (string description, string key, string value, string type, string data)[]
        {
            ("Disabling telemetry...",
                @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", "REG_DWORD", "0"),
            ("Disabling advertising ID...",
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", "REG_DWORD", "0"),
            ("Disabling tips and suggestions...",
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338393Enabled", "REG_DWORD", "0"),
            ("Disabling suggested content...",
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353694Enabled", "REG_DWORD", "0"),
            ("Disabling notification suggestions...",
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353696Enabled", "REG_DWORD", "0"),
            ("Disabling Start menu suggestions...",
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", "REG_DWORD", "0"),
            ("Disabling lock screen tips...",
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenEnabled", "REG_DWORD", "0"),
            ("Disabling lock screen overlay...",
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenOverlayEnabled", "REG_DWORD", "0"),
            ("Disabling app-launch tracking...",
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", "REG_DWORD", "0"),
            ("Disabling Bing search in Start...",
                @"HKCU\SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", "REG_DWORD", "1"),
        };

        foreach (var (description, key, value, type, data) in commands)
        {
            progress?.Report(description);
            var result = await RunProcessAsync("reg",
                $@"add ""{key}"" /v {value} /t {type} /d {data} /f");
            if (result != 0)
                allOk = false;
        }

        progress?.Report(allOk ? "Privacy tweaks applied successfully" : "Some privacy tweaks failed — may require admin privileges");
        return allOk;
    }

    /// <summary>
    /// Gets a dictionary of installed AppX package names → full names.
    /// Uses Windows PowerShell 5.1 for AppX cmdlet compatibility.
    /// </summary>
    private static Dictionary<string, string> GetInstalledPackageNames()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = GetWindowsPowerShellPath(),
                Arguments = "-NoProfile -NoLogo -Command \"Get-AppxPackage -AllUsers | Select-Object -Property Name,PackageFullName | ConvertTo-Csv -NoTypeInformation\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return result;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(TimeSpan.FromSeconds(60));

            // Parse CSV output — skip header row
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines.Skip(1))
            {
                // CSV format: "Name","PackageFullName"
                var parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    var name = parts[0].Trim('"');
                    var fullName = parts[1].Trim('"');
                    if (!string.IsNullOrWhiteSpace(name))
                        result.TryAdd(name, fullName);
                }
            }
        }
        catch
        {
            // PowerShell not available or AppX cmdlets failed
        }

        return result;
    }

    /// <summary>
    /// Runs a PowerShell script via Windows PowerShell 5.1.
    /// -ExecutionPolicy Bypass is required because the system's execution policy may be
    /// Restricted or AllSigned, which would block our inline commands. This is standard
    /// practice for admin tools (PowerToys, winget, DISM GUI wrappers all do the same).
    /// The tool already runs elevated (requireAdministrator manifest), so this does not
    /// grant any additional privileges beyond what the process already has.
    /// </summary>
    private static async Task<int> RunPowerShellAsync(string script, int timeoutSeconds = 120)
    {
        return await RunProcessAsync(GetWindowsPowerShellPath(),
            $"-NoProfile -NoLogo -ExecutionPolicy Bypass -Command \"{script}\"",
            timeoutSeconds);
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

    /// <summary>
    /// Returns the path to Windows PowerShell 5.1 (not PowerShell 7).
    /// AppX cmdlets have known issues in PowerShell 7.
    /// </summary>
    private static string GetWindowsPowerShellPath()
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
    }
}

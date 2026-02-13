using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using Microsoft.Win32;
using WinRegistry = Microsoft.Win32.Registry;

namespace CORCleanup.Core.Services.Tools;

[SupportedOSPlatform("windows")]
public sealed class AntivirusService : IAntivirusService
{
    // ================================================================
    // Known AV Vendor Database
    // ================================================================

    private sealed record KnownAvVendor(
        string VendorName,
        string[] ServiceNames,
        string[] RegistrySubKeys,
        string[] FolderNames,
        string? RemovalToolUrl);

    private static readonly KnownAvVendor[] KnownVendors =
    [
        new("Norton", ["NortonSecurity", "N360", "Norton AntiVirus", "Norton Security", "navapsvc", "ccSetMgr", "ccEvtMgr", "SepMasterService"],
            [@"Norton", @"Symantec", @"NortonInstaller"],
            ["Norton", "Norton Security", "Norton AntiVirus", "Symantec", "NortonInstaller"],
            "https://support.norton.com/sp/en/us/home/current/solutions/v60392881"),

        new("McAfee", ["McAfeeFramework", "mcshield", "mfemms", "mfevtp", "McAPExe", "masvc", "macmnsvc", "mfewc", "McAWFwk"],
            [@"McAfee", @"McAfee.com"],
            ["McAfee", "McAfee.com", "McAfee Security", "McAfee Online Backup"],
            "https://www.mcafee.com/support/?page=shell&shell=article-view&articleId=TS101331"),

        new("Kaspersky", ["AVP", "avp", "klnagent", "KAVFS", "KAVFSGT", "kavfsslp", "kavsvc"],
            [@"KasperskyLab", @"Kaspersky Lab"],
            ["Kaspersky Lab", "KasperskyLab"],
            "https://support.kaspersky.com/common/uninstall/1464"),

        new("AVG", ["avgwd", "AVGSvc", "avgsvcx", "avgfws"],
            [@"AVG", @"AVG\Antivirus"],
            ["AVG", @"AVG\Antivirus"],
            "https://support.avg.com/SupportArticleView?l=en&urlName=avg-clear"),

        new("Avast", ["avast! Antivirus", "AvastSvc", "aswbIDSAgent", "avast! Firewall"],
            [@"AVAST Software", @"Avast Software"],
            ["AVAST Software", "Avast Software", "Avast"],
            "https://support.avast.com/en-ww/article/antivirus-uninstall-utility/"),

        new("ESET", ["ekrn", "essvc", "egui", "EhttpSrv", "ESHASRV"],
            [@"ESET"],
            ["ESET"],
            "https://support.eset.com/en/kb2289-uninstall-eset-manually-using-the-eset-uninstaller-tool"),

        new("Bitdefender", ["VSSERV", "bdredline", "bdagent", "updatesrv", "EPSecurityService", "EPIntegrationService", "EPProtectedService"],
            [@"Bitdefender", @"Bitdefender Agent"],
            ["Bitdefender", "Bitdefender Agent"],
            "https://www.bitdefender.co.uk/consumer/support/answer/13427/"),

        new("Malwarebytes", ["MBAMService", "MBAMProtection", "mbamtray"],
            [@"Malwarebytes"],
            ["Malwarebytes"],
            "https://support.malwarebytes.com/hc/en-us/articles/360039023473"),

        new("Trend Micro", ["Amsp", "PcCtlCom", "TmFilter", "TMLWCSService", "taborpa"],
            [@"Trend Micro", @"TrendMicro"],
            ["Trend Micro", "TrendMicro"],
            "https://helpcenter.trendmicro.com/en-us/article/tmka-18498"),

        new("Webroot", ["WRSVC", "WRCoreService", "WRSkyClient"],
            [@"Webroot", @"WRData", @"WRCore"],
            ["Webroot", "WRData", "WRCore"],
            "https://answers.webroot.com/Webroot/ukp.aspx?pid=17&app=vw&vw=1&login=1&json=1&solutionid=1044"),

        new("Sophos", ["SAVService", "SAVAdminService", "Sophos Agent", "Sophos AutoUpdate Service", "Sophos MCS Agent", "HitmanPro.Alert"],
            [@"Sophos", @"HitmanPro"],
            ["Sophos", "HitmanPro", "HitmanPro.Alert"],
            "https://support.sophos.com/support/s/article/KB-000033686"),

        new("Comodo", ["CmdAgent", "cmdvirth", "CisTray"],
            [@"COMODO", @"Comodo"],
            ["COMODO", "Comodo"],
            null),

        new("F-Secure", ["FSGKHS", "FSORSPClient", "FSMA", "F-Secure Ultralight SDK"],
            [@"F-Secure"],
            ["F-Secure"],
            "https://www.f-secure.com/en/support/uninstallation-tool"),

        new("Panda", ["PavFnSvr", "PSUAService", "PandaAgent"],
            [@"Panda Security", @"Panda"],
            ["Panda Security", "Panda"],
            null),

        new("BullGuard", ["BullGuardCore", "BullGuardUpdate", "BullGuardScanner"],
            [@"BullGuard"],
            ["BullGuard", "BullGuard Ltd"],
            null),

        new("ZoneAlarm", ["vsmon", "ISWSVC", "ZAPrivacyService"],
            [@"Zone Labs", @"CheckPoint\ZoneAlarm"],
            ["Zone Labs", "CheckPoint"],
            "https://www.zonealarm.com/support/uninstall-steps"),

        new("G Data", ["GDScan", "AVKProxy", "AVKService", "GDFwSvc"],
            [@"G Data", @"G DATA"],
            ["G Data", "G DATA"],
            null),

        new("Vipre", ["SBAMSvc", "SBPIMSvc"],
            [@"Vipre", @"VIPRE", @"ThreatTrack Security"],
            ["Vipre", "VIPRE", "ThreatTrack Security"],
            null),

        new("K7", ["K7TSMngr", "K7FWSrv", "K7RTScan"],
            [@"K7 Computing"],
            ["K7 Computing"],
            null),

        new("Windows Defender", ["WinDefend", "WdNisSvc"],
            [@"Windows Defender"],
            [],
            null)
    ];

    // ================================================================
    // Public API
    // ================================================================

    public async Task<List<AntivirusProduct>> ScanAsync(IProgress<string>? progress = null)
    {
        var products = new List<AntivirusProduct>();
        var activeVendorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Layer 1: WMI SecurityCenter2
        progress?.Report("Querying Windows Security Centre...");
        var wmiProducts = await ScanWmiAsync();
        foreach (var product in wmiProducts)
        {
            products.Add(product);
            var vendor = MatchVendorName(product.ProductName);
            if (vendor is not null)
                activeVendorNames.Add(vendor);
        }

        // Layer 2: Registry orphan scan
        progress?.Report("Scanning registry for orphaned AV entries...");
        ScanRegistryRemnants(products, activeVendorNames);

        // Layer 3: Service scan
        progress?.Report("Checking for orphaned AV services...");
        await ScanServicesAsync(products, activeVendorNames);

        // Layer 4: File system remnant scan
        progress?.Report("Scanning for remnant folders...");
        ScanFileSystemRemnants(products, activeVendorNames);

        // Detect conflicts (multiple active non-Defender AV products)
        DetectConflicts(products);

        progress?.Report("Scan complete");
        return products;
    }

    // ================================================================
    // Layer 1: WMI SecurityCenter2
    // ================================================================

    private static async Task<List<AntivirusProduct>> ScanWmiAsync()
    {
        var products = new List<AntivirusProduct>();

        var script = "Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct | " +
                     "ForEach-Object { \"$($_.displayName)|$($_.productState)|$($_.pathToSignedProductExe)|$($_.pathToSignedReportingExe)\" }";

        var output = await RunPowerShellAsync(script);
        if (string.IsNullOrWhiteSpace(output))
            return products;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|');
            if (parts.Length < 2) continue;

            var displayName = parts[0].Trim();
            if (!uint.TryParse(parts[1].Trim(), out var productState)) continue;

            var installPath = parts.Length > 2 ? parts[2].Trim() : null;
            if (string.IsNullOrEmpty(installPath)) installPath = null;

            // Decode productState bitmask
            // Format: XXYYZZ where XX=type, YY=scanner state, ZZ=definition state
            var hex = productState.ToString("X6");
            bool enabled = hex.Length >= 4 && hex.Substring(2, 2) is "10" or "11";
            bool upToDate = hex.Length >= 6 && hex.Substring(4, 2) == "00";

            var vendor = FindVendor(displayName);
            var status = enabled ? AntivirusStatus.Active : AntivirusStatus.Installed;

            products.Add(new AntivirusProduct
            {
                ProductName = displayName,
                Status = status,
                IsEnabled = enabled,
                IsUpToDate = upToDate,
                Publisher = vendor?.VendorName,
                InstallPath = installPath,
                RemovalToolUrl = vendor?.RemovalToolUrl
            });
        }

        return products;
    }

    // ================================================================
    // Layer 2: Registry Remnant Scan
    // ================================================================

    private static void ScanRegistryRemnants(List<AntivirusProduct> products, HashSet<string> activeVendorNames)
    {
        var remnantsByVendor = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        string[] hives = [@"SOFTWARE", @"SOFTWARE\WOW6432Node"];

        foreach (var vendor in KnownVendors)
        {
            if (vendor.VendorName == "Windows Defender") continue;
            if (activeVendorNames.Contains(vendor.VendorName)) continue;

            var registryRemnants = new List<string>();

            foreach (var hive in hives)
            {
                foreach (var subKey in vendor.RegistrySubKeys)
                {
                    var keyPath = $@"{hive}\{subKey}";
                    try
                    {
                        using var key = WinRegistry.LocalMachine.OpenSubKey(keyPath);
                        if (key is not null)
                            registryRemnants.Add($@"HKLM\{keyPath}");
                    }
                    catch
                    {
                        // Access denied or other registry error — skip
                    }
                }
            }

            // Check uninstall keys for orphaned entries
            foreach (var hive in hives)
            {
                var uninstallPath = $@"{hive}\Microsoft\Windows\CurrentVersion\Uninstall";
                try
                {
                    using var uninstallKey = WinRegistry.LocalMachine.OpenSubKey(uninstallPath);
                    if (uninstallKey is null) continue;

                    foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var entry = uninstallKey.OpenSubKey(subKeyName);
                            var displayName = entry?.GetValue("DisplayName") as string;
                            var publisher = entry?.GetValue("Publisher") as string;

                            if (displayName is null && publisher is null) continue;

                            bool matches = vendor.RegistrySubKeys.Any(rsk =>
                                (displayName?.Contains(rsk, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                (publisher?.Contains(vendor.VendorName, StringComparison.OrdinalIgnoreCase) ?? false));

                            if (matches)
                                registryRemnants.Add($@"HKLM\{uninstallPath}\{subKeyName} ({displayName ?? "Unknown"})");
                        }
                        catch { }
                    }
                }
                catch { }
            }

            if (registryRemnants.Count > 0)
                remnantsByVendor[vendor.VendorName] = registryRemnants;
        }

        // Merge into existing products or create new Remnant entries
        foreach (var (vendorName, regKeys) in remnantsByVendor)
        {
            var existing = products.FirstOrDefault(p =>
                p.Publisher?.Equals(vendorName, StringComparison.OrdinalIgnoreCase) == true);

            if (existing is not null)
            {
                existing.RemnantRegistryKeys.AddRange(regKeys);
            }
            else
            {
                var vendor = KnownVendors.FirstOrDefault(v =>
                    v.VendorName.Equals(vendorName, StringComparison.OrdinalIgnoreCase));

                products.Add(new AntivirusProduct
                {
                    ProductName = $"{vendorName} (remnant)",
                    Status = AntivirusStatus.Remnant,
                    Publisher = vendorName,
                    RemovalToolUrl = vendor?.RemovalToolUrl,
                    RemnantRegistryKeys = regKeys
                });
            }
        }
    }

    // ================================================================
    // Layer 3: Service Scan
    // ================================================================

    private static async Task ScanServicesAsync(List<AntivirusProduct> products, HashSet<string> activeVendorNames)
    {
        var script = "Get-Service | ForEach-Object { \"$($_.Name)|$($_.Status)|$($_.DisplayName)\" }";
        var output = await RunPowerShellAsync(script);
        if (string.IsNullOrWhiteSpace(output)) return;

        var foundServices = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|');
            if (parts.Length < 3) continue;

            var serviceName = parts[0].Trim();

            foreach (var vendor in KnownVendors)
            {
                if (vendor.VendorName == "Windows Defender") continue;

                if (vendor.ServiceNames.Any(sn => sn.Equals(serviceName, StringComparison.OrdinalIgnoreCase)))
                {
                    // Only flag as remnant if this vendor has no active WMI registration
                    if (activeVendorNames.Contains(vendor.VendorName)) break;

                    if (!foundServices.ContainsKey(vendor.VendorName))
                        foundServices[vendor.VendorName] = [];

                    foundServices[vendor.VendorName].Add($"{serviceName} ({parts[2].Trim()})");
                    break;
                }
            }
        }

        foreach (var (vendorName, services) in foundServices)
        {
            var existing = products.FirstOrDefault(p =>
                p.Publisher?.Equals(vendorName, StringComparison.OrdinalIgnoreCase) == true);

            if (existing is not null)
            {
                existing.RemnantServices.AddRange(services);
            }
            else
            {
                var vendor = KnownVendors.FirstOrDefault(v =>
                    v.VendorName.Equals(vendorName, StringComparison.OrdinalIgnoreCase));

                products.Add(new AntivirusProduct
                {
                    ProductName = $"{vendorName} (remnant)",
                    Status = AntivirusStatus.Remnant,
                    Publisher = vendorName,
                    RemovalToolUrl = vendor?.RemovalToolUrl,
                    RemnantServices = services
                });
            }
        }
    }

    // ================================================================
    // Layer 4: File System Remnant Scan
    // ================================================================

    private static void ScanFileSystemRemnants(List<AntivirusProduct> products, HashSet<string> activeVendorNames)
    {
        string[] searchRoots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        ];

        var remnantsByVendor = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var vendor in KnownVendors)
        {
            if (vendor.VendorName == "Windows Defender") continue;
            if (vendor.FolderNames.Length == 0) continue;
            if (activeVendorNames.Contains(vendor.VendorName)) continue;

            var folderRemnants = new List<string>();

            foreach (var root in searchRoots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                foreach (var folderName in vendor.FolderNames)
                {
                    var path = Path.Combine(root, folderName);
                    if (Directory.Exists(path))
                        folderRemnants.Add(path);
                }
            }

            if (folderRemnants.Count > 0)
                remnantsByVendor[vendor.VendorName] = folderRemnants;
        }

        foreach (var (vendorName, paths) in remnantsByVendor)
        {
            var existing = products.FirstOrDefault(p =>
                p.Publisher?.Equals(vendorName, StringComparison.OrdinalIgnoreCase) == true);

            if (existing is not null)
            {
                existing.RemnantPaths.AddRange(paths);
            }
            else
            {
                var vendor = KnownVendors.FirstOrDefault(v =>
                    v.VendorName.Equals(vendorName, StringComparison.OrdinalIgnoreCase));

                products.Add(new AntivirusProduct
                {
                    ProductName = $"{vendorName} (remnant)",
                    Status = AntivirusStatus.Remnant,
                    Publisher = vendorName,
                    RemovalToolUrl = vendor?.RemovalToolUrl,
                    RemnantPaths = paths
                });
            }
        }
    }

    // ================================================================
    // Conflict Detection
    // ================================================================

    private static void DetectConflicts(List<AntivirusProduct> products)
    {
        var activeNonDefender = products
            .Where(p => p.Status == AntivirusStatus.Active &&
                        p.Publisher != "Windows Defender")
            .ToList();

        if (activeNonDefender.Count <= 1) return;

        // Multiple active third-party AV = conflict
        // We can't mutate the Status (it's init-only), so we replace the entries
        for (int i = 0; i < products.Count; i++)
        {
            if (products[i].Status == AntivirusStatus.Active &&
                products[i].Publisher != "Windows Defender" &&
                activeNonDefender.Contains(products[i]))
            {
                var original = products[i];
                products[i] = new AntivirusProduct
                {
                    ProductName = original.ProductName,
                    Status = AntivirusStatus.Conflict,
                    IsEnabled = original.IsEnabled,
                    IsUpToDate = original.IsUpToDate,
                    Publisher = original.Publisher,
                    InstallPath = original.InstallPath,
                    Version = original.Version,
                    RemovalToolUrl = original.RemovalToolUrl,
                    RemnantPaths = original.RemnantPaths,
                    RemnantServices = original.RemnantServices,
                    RemnantRegistryKeys = original.RemnantRegistryKeys
                };
            }
        }
    }

    // ================================================================
    // Vendor Matching Helpers
    // ================================================================

    private static KnownAvVendor? FindVendor(string productName)
    {
        foreach (var vendor in KnownVendors)
        {
            if (productName.Contains(vendor.VendorName, StringComparison.OrdinalIgnoreCase))
                return vendor;

            // Check alternate names in registry sub-keys (e.g. "Symantec" for Norton)
            if (vendor.RegistrySubKeys.Any(rsk =>
                productName.Contains(rsk, StringComparison.OrdinalIgnoreCase)))
                return vendor;
        }

        return null;
    }

    private static string? MatchVendorName(string productName)
    {
        return FindVendor(productName)?.VendorName;
    }

    // ================================================================
    // PowerShell Execution (matches DebloatService pattern)
    // ================================================================

    private static async Task<string> RunPowerShellAsync(string script)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process is null) return "";

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return output;
        }
        catch
        {
            return "";
        }
    }
}

using System.Management;
using System.Runtime.Versioning;
using System.ServiceProcess;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Core.Security;

namespace CORCleanup.Core.Services.Admin;

/// <summary>
/// Manages Windows services via ServiceController + WMI for extra metadata.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ServicesManagerService : IServicesManagerService
{
    // Services commonly safe to disable on Win11 — bloatware/telemetry
    private static readonly HashSet<string> SafeToDisable = new(StringComparer.OrdinalIgnoreCase)
    {
        "DiagTrack",                    // Connected User Experiences and Telemetry
        "dmwappushservice",             // WAP Push Message Routing (telemetry)
        "MapsBroker",                   // Downloaded Maps Manager
        "lfsvc",                        // Geolocation Service
        "RetailDemo",                   // Retail Demo Service
        "WMPNetworkSvc",               // Windows Media Player Network Sharing
        "XblAuthManager",              // Xbox Live Auth Manager
        "XblGameSave",                 // Xbox Live Game Save
        "XboxGipSvc",                  // Xbox Accessory Management
        "XboxNetApiSvc",               // Xbox Live Networking
        "WSearch",                     // Windows Search (can disable on SSDs)
        "SysMain",                     // Superfetch (unnecessary on SSDs)
        "Fax",                         // Fax Service
    };

    private static readonly Dictionary<string, string> SafeToDisableReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DiagTrack"] = "Microsoft telemetry — disabling stops data collection",
        ["dmwappushservice"] = "Telemetry routing — safe to disable",
        ["MapsBroker"] = "Offline maps — rarely used, safe to disable",
        ["lfsvc"] = "Location services — disable unless using location-aware apps",
        ["RetailDemo"] = "Shop display mode — never needed on real machines",
        ["WMPNetworkSvc"] = "Media sharing — disable unless streaming to devices",
        ["XblAuthManager"] = "Xbox services — disable if not gaming",
        ["XblGameSave"] = "Xbox save sync — disable if not gaming",
        ["XboxGipSvc"] = "Xbox accessories — disable if no Xbox controller",
        ["XboxNetApiSvc"] = "Xbox networking — disable if not gaming",
        ["WSearch"] = "Windows Search indexer — disable on SSDs for less disk activity",
        ["SysMain"] = "Superfetch — unnecessary on SSD machines",
        ["Fax"] = "Fax service — rarely used in modern environments",
    };

    public Task<List<ServiceEntry>> GetServicesAsync() => Task.Run(() =>
    {
        var services = ServiceController.GetServices();
        var results = new List<ServiceEntry>(services.Length);

        // Get extra metadata (description, path) via WMI in one query
        var wmiData = new Dictionary<string, (string? Description, string? PathName)>(
            StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Description, PathName FROM Win32_Service");
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    var name = obj["Name"]?.ToString();
                    if (name is not null)
                    {
                        wmiData[name] = (
                            obj["Description"]?.ToString(),
                            obj["PathName"]?.ToString());
                    }
                }
            }
        }
        catch
        {
            // WMI may be unavailable — continue with basic data
        }

        try
        {
            foreach (var svc in services)
            {
                try
                {
                    wmiData.TryGetValue(svc.ServiceName, out var extra);

                    var category = CategoriseService(svc.ServiceName, extra.PathName);
                    var isSafe = SafeToDisable.Contains(svc.ServiceName);

                    results.Add(new ServiceEntry
                    {
                        ServiceName = svc.ServiceName,
                        DisplayName = svc.DisplayName,
                        Status = svc.Status,
                        StartType = svc.StartType,
                        Description = extra.Description,
                        PathToExecutable = extra.PathName,
                        Category = category,
                        IsSafeToDisable = isSafe,
                        SafeToDisableReason = isSafe
                            ? SafeToDisableReasons.GetValueOrDefault(svc.ServiceName)
                            : null
                    });
                }
                catch
                {
                    // Skip services that throw on property access
                }
            }
        }
        finally
        {
            // ServiceController implements IDisposable — dispose each to release SCM handles
            foreach (var svc in services)
                svc.Dispose();
        }

        return results.OrderBy(s => s.DisplayName).ToList();
    });

    public Task StartServiceAsync(string serviceName) => Task.Run(() =>
    {
        using var svc = new ServiceController(serviceName);
        if (svc.Status != ServiceControllerStatus.Running)
        {
            svc.Start();
            svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        }
    });

    public Task StopServiceAsync(string serviceName) => Task.Run(() =>
    {
        using var svc = new ServiceController(serviceName);
        if (svc.Status != ServiceControllerStatus.Stopped && svc.CanStop)
        {
            svc.Stop();
            svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
    });

    public async Task RestartServiceAsync(string serviceName)
    {
        await StopServiceAsync(serviceName);
        await StartServiceAsync(serviceName);
    }

    public Task SetStartupTypeAsync(string serviceName, ServiceStartMode startMode) =>
        Task.Run(() =>
        {
            if (!InputSanitiser.IsValidServiceName(serviceName))
                throw new ArgumentException("Invalid service name.", nameof(serviceName));

            // ServiceController doesn't expose startup type modification
            // Use WMI ChangeStartMode method instead
            using var obj = new ManagementObject($"Win32_Service.Name='{InputSanitiser.EscapeWql(serviceName)}'");
            obj.InvokeMethod("ChangeStartMode", new object[] { startMode.ToString() });
        });

    private static ServiceCategory CategoriseService(string serviceName, string? pathName)
    {
        if (pathName is null) return ServiceCategory.ThirdParty;

        if (pathName.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase) ||
            pathName.Contains("svchost.exe", StringComparison.OrdinalIgnoreCase))
        {
            // Distinguish core vs optional Microsoft services
            return SafeToDisable.Contains(serviceName)
                ? ServiceCategory.MicrosoftOptional
                : ServiceCategory.MicrosoftCore;
        }

        if (pathName.Contains(@"\Microsoft\", StringComparison.OrdinalIgnoreCase))
            return ServiceCategory.MicrosoftOptional;

        return ServiceCategory.ThirdParty;
    }
}

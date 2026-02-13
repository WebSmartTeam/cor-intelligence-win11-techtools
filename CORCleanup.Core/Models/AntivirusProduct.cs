namespace CORCleanup.Core.Models;

public enum AntivirusStatus
{
    Active,
    Installed,
    Remnant,
    Conflict
}

public sealed class AntivirusProduct
{
    public required string ProductName { get; init; }
    public required AntivirusStatus Status { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsUpToDate { get; init; }
    public string? Publisher { get; init; }
    public string? InstallPath { get; init; }
    public string? Version { get; init; }
    public string? RemovalToolUrl { get; init; }
    public List<string> RemnantPaths { get; init; } = [];
    public List<string> RemnantServices { get; init; } = [];
    public List<string> RemnantRegistryKeys { get; init; } = [];

    public string StatusDisplay => Status switch
    {
        AntivirusStatus.Active => "Active",
        AntivirusStatus.Installed => "Installed",
        AntivirusStatus.Remnant => "Remnant",
        AntivirusStatus.Conflict => "Conflict",
        _ => Status.ToString()
    };

    public string StatusColour => Status switch
    {
        AntivirusStatus.Active => "#4CAF50",
        AntivirusStatus.Installed => "#2196F3",
        AntivirusStatus.Remnant => "#FF9800",
        AntivirusStatus.Conflict => "#F44336",
        _ => "#888888"
    };

    public int RemnantCount => RemnantPaths.Count + RemnantServices.Count + RemnantRegistryKeys.Count;
    public bool HasRemnants => RemnantCount > 0;

    public string RemnantSummary
    {
        get
        {
            if (!HasRemnants) return "None";
            var parts = new List<string>();
            if (RemnantPaths.Count > 0) parts.Add($"{RemnantPaths.Count} folder(s)");
            if (RemnantServices.Count > 0) parts.Add($"{RemnantServices.Count} service(s)");
            if (RemnantRegistryKeys.Count > 0) parts.Add($"{RemnantRegistryKeys.Count} registry key(s)");
            return string.Join(", ", parts);
        }
    }

    public string EnabledDisplay => IsEnabled ? "Yes" : "No";
    public string UpToDateDisplay => IsUpToDate ? "Yes" : "N/A";
}

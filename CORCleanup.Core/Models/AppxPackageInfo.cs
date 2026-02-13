namespace CORCleanup.Core.Models;

public enum DebloatCategory
{
    AiCopilot,
    Xbox,
    Entertainment,
    Communication,
    Productivity,
    SystemExtras,
    Privacy
}

public enum DebloatSafety
{
    Safe,       // Green — safe to remove in virtually all scenarios
    Review,     // Amber — technician should verify per client
    Caution     // Red — removing may break expected functionality
}

/// <summary>
/// An AppX package that can be removed as bloatware.
/// </summary>
public sealed class AppxPackageInfo
{
    public required string PackageName { get; init; }
    public required string FriendlyName { get; init; }
    public required DebloatCategory Category { get; init; }
    public required DebloatSafety Safety { get; init; }
    public required string Description { get; init; }
    public bool IsInstalled { get; set; }
    public bool IsSelected { get; set; }
    public string? PackageFullName { get; set; }

    public string CategoryDisplay => Category switch
    {
        DebloatCategory.AiCopilot => "AI / Copilot",
        DebloatCategory.Xbox => "Xbox / Gaming",
        DebloatCategory.Entertainment => "Entertainment",
        DebloatCategory.Communication => "Communication",
        DebloatCategory.Productivity => "Productivity",
        DebloatCategory.SystemExtras => "System Extras",
        DebloatCategory.Privacy => "Privacy / Telemetry",
        _ => Category.ToString()
    };

    public string SafetyDisplay => Safety switch
    {
        DebloatSafety.Safe => "Safe",
        DebloatSafety.Review => "Review",
        DebloatSafety.Caution => "Caution",
        _ => Safety.ToString()
    };

    public string StatusDisplay => IsInstalled ? "Installed" : "Not Found";
}

/// <summary>
/// Result of a debloat removal operation.
/// </summary>
public sealed class DebloatResult
{
    public required int TotalSelected { get; init; }
    public required int Removed { get; init; }
    public required int Failed { get; init; }
    public required int NotFound { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> RemovedPackages { get; init; } = new();
}

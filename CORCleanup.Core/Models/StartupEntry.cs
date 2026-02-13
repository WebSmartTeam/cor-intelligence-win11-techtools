namespace CORCleanup.Core.Models;

public enum StartupSource
{
    RegistryRun,
    RegistryRunOnce,
    StartupFolder,
    ScheduledTask,
    Service
}

public enum StartupImpact
{
    High,
    Medium,
    Low,
    Unknown
}

public sealed class StartupEntry
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public required StartupSource Source { get; init; }
    public required bool IsEnabled { get; init; }
    public required bool IsMicrosoft { get; init; }
    public string? Publisher { get; init; }
    public string? Description { get; init; }
    public StartupImpact Impact { get; init; } = StartupImpact.Unknown;
    public bool IsSigned { get; init; }
    public string? RegistryPath { get; init; }
}

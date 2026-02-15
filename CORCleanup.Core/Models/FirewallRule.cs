namespace CORCleanup.Core.Models;

public sealed class FirewallRule
{
    public required string Name { get; init; }
    public required string Direction { get; init; }
    public required string Action { get; init; }
    public required string Profile { get; init; }
    public required bool Enabled { get; init; }
    public string Protocol { get; init; } = "Any";
    public string LocalPort { get; init; } = "Any";
    public string RemotePort { get; init; } = "Any";
    public string Program { get; init; } = "Any";
    public string Description { get; init; } = "";

    public string StatusDisplay => Enabled ? "Enabled" : "Disabled";
    public string ActionDisplay => Action;
}

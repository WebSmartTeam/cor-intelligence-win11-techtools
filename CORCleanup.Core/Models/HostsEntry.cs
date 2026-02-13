namespace CORCleanup.Core.Models;

public sealed class HostsEntry
{
    public required string IpAddress { get; set; }
    public required string Hostname { get; set; }
    public string? Comment { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int LineNumber { get; init; }

    public string StatusDisplay => IsEnabled ? "Active" : "Disabled";
}

namespace CORCleanup.Core.Models;

public sealed class TracerouteHop
{
    public required int HopNumber { get; init; }
    public required string Address { get; init; }
    public string? Hostname { get; init; }
    public required long RoundtripMs1 { get; init; }
    public required long RoundtripMs2 { get; init; }
    public required long RoundtripMs3 { get; init; }
    public bool TimedOut { get; init; }

    public string DisplayAddress => Hostname is not null ? $"{Hostname} [{Address}]" : Address;

    public long AvgMs => TimedOut ? 0 : (RoundtripMs1 + RoundtripMs2 + RoundtripMs3) / 3;

    public string LatencyDisplay => TimedOut
        ? "* * *"
        : $"{RoundtripMs1}ms  {RoundtripMs2}ms  {RoundtripMs3}ms";

    public string Status => TimedOut ? "Timeout" : AvgMs > 100 ? "Slow" : "OK";
}

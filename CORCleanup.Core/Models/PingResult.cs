using System.Net.NetworkInformation;

namespace CORCleanup.Core.Models;

public sealed class PingResult
{
    public required DateTime Timestamp { get; init; }
    public required IPStatus Status { get; init; }
    public required long RoundtripMs { get; init; }
    public int Ttl { get; init; }
    public required string Target { get; init; }

    public bool IsSuccess => Status == IPStatus.Success;
}

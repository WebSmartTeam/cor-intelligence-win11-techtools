namespace CORCleanup.Core.Models;

public sealed class WifiProfile
{
    public required string Ssid { get; init; }
    public required string SecurityType { get; init; }
    public string? Password { get; init; }
    public bool PasswordRetrieved => Password is not null;
}

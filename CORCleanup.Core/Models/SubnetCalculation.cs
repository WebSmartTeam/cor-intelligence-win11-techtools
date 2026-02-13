namespace CORCleanup.Core.Models;

public sealed class SubnetCalculation
{
    public required string InputCidr { get; init; }
    public required string NetworkAddress { get; init; }
    public required string BroadcastAddress { get; init; }
    public required string SubnetMask { get; init; }
    public required string WildcardMask { get; init; }
    public required string FirstUsable { get; init; }
    public required string LastUsable { get; init; }
    public required long TotalHosts { get; init; }
    public required long UsableHosts { get; init; }
    public required int CidrNotation { get; init; }

    public string HostRange => $"{FirstUsable} - {LastUsable}";
    public string TotalDisplay => $"{UsableHosts:N0} usable of {TotalHosts:N0} total";
}

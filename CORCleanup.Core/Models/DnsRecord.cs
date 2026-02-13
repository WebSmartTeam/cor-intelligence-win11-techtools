namespace CORCleanup.Core.Models;

public sealed class DnsRecord
{
    public required string Type { get; init; }
    public required string Name { get; init; }
    public required string Value { get; init; }
    public int? Ttl { get; init; }
    public int? Priority { get; init; }
}

public sealed class DnsLookupResult
{
    public required string Domain { get; init; }
    public required string DnsServer { get; init; }
    public required string RecordType { get; init; }
    public required List<DnsRecord> Records { get; init; }
    public required long QueryTimeMs { get; init; }
    public string? Error { get; init; }
    public bool IsSuccess => Error is null;
}

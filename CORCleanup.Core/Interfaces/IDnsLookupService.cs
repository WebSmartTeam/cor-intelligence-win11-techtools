using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IDnsLookupService
{
    Task<DnsLookupResult> LookupAsync(
        string domain,
        string recordType = "A",
        string? dnsServer = null,
        CancellationToken ct = default);

    Task<List<DnsLookupResult>> PropagationCheckAsync(
        string domain,
        string recordType = "A",
        CancellationToken ct = default);
}

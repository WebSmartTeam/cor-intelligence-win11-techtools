using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IPortScannerService
{
    IAsyncEnumerable<PortScanResult> ScanPortsAsync(
        string host,
        IEnumerable<int> ports,
        int timeoutMs = 2000,
        CancellationToken ct = default);

    Task<List<LocalPortEntry>> GetLocalPortsAsync(CancellationToken ct = default);
}

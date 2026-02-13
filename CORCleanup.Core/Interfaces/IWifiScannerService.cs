using System.Runtime.CompilerServices;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IWifiScannerService
{
    Task<List<WifiNetwork>> ScanNetworksAsync(CancellationToken ct = default);

    IAsyncEnumerable<WifiSignalReading> MonitorSignalAsync(
        int intervalMs = 1000,
        [EnumeratorCancellation] CancellationToken ct = default);

    List<ChannelUsageInfo> GetChannelUsage(List<WifiNetwork> networks);
}

using System.Net.NetworkInformation;

namespace CORCleanup.Core.Interfaces;

public interface IPingService
{
    IAsyncEnumerable<Models.PingResult> ContinuousPingAsync(
        string target,
        int intervalMs = 1000,
        CancellationToken ct = default);

    Task<Models.PingResult> SinglePingAsync(string target, int timeoutMs = 3000);
}

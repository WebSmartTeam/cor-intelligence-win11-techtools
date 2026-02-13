using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;

namespace CORCleanup.Core.Interfaces;

public interface IPingService
{
    IAsyncEnumerable<Models.PingResult> ContinuousPingAsync(
        string target,
        int intervalMs = 1000,
        [EnumeratorCancellation] CancellationToken ct = default);

    Task<Models.PingResult> SinglePingAsync(string target, int timeoutMs = 3000);
}

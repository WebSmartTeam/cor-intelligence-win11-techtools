using System.Runtime.CompilerServices;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface ITracerouteService
{
    IAsyncEnumerable<TracerouteHop> TraceAsync(
        string target,
        int maxHops = 30,
        int timeoutMs = 3000,
        [EnumeratorCancellation] CancellationToken ct = default);
}

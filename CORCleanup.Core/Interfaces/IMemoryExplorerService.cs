using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IMemoryExplorerService
{
    /// <summary>
    /// Gets system memory overview (RAM and page file usage).
    /// </summary>
    Task<MemoryInfo> GetMemoryInfoAsync();

    /// <summary>
    /// Gets the top N memory consumers by working set size.
    /// </summary>
    Task<List<MemoryConsumer>> GetTopConsumersAsync(int top = 30, CancellationToken ct = default);
}

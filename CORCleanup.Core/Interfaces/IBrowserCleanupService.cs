using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IBrowserCleanupService
{
    Task<List<BrowserCleanupItem>> ScanBrowserDataAsync();
    Task<long> CleanSelectedAsync(List<BrowserCleanupItem> items, CancellationToken ct = default);
    List<string> GetRunningBrowsers();
}

using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface ICleanupService
{
    Task<List<CleanupItem>> ScanAsync(CancellationToken ct = default);
    Task<CleanupResult> CleanAsync(
        IEnumerable<CleanupCategory> selectedCategories,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}

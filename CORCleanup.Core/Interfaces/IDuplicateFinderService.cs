using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IDuplicateFinderService
{
    /// <summary>
    /// Scans <paramref name="path"/> for duplicate files using a multi-stage algorithm:
    /// 1. Group by file size  2. Partial SHA-256 (first 4 KB)  3. Full SHA-256.
    /// Results are yielded as groups are confirmed, enabling progressive UI updates.
    /// </summary>
    /// <param name="path">Root directory to scan.</param>
    /// <param name="minSizeBytes">Ignore files smaller than this (default 1 KB).</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<DuplicateGroup> FindDuplicatesAsync(
        string path, long minSizeBytes = 1024, CancellationToken ct = default);

    /// <summary>
    /// Deletes a file to the Windows Recycle Bin (recoverable).
    /// </summary>
    Task DeleteToRecycleBinAsync(string filePath);
}

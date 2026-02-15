using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IDiskAnalyserService
{
    /// <summary>
    /// Recursively analyses folder sizes starting from <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Root directory to analyse.</param>
    /// <param name="maxDepth">Maximum folder depth to recurse (0 = root only).</param>
    /// <param name="ct">Cancellation token for long-running scans.</param>
    /// <returns>Tree of folder sizes with children populated to <paramref name="maxDepth"/>.</returns>
    Task<FolderSizeInfo> AnalyseFolderAsync(string path, int maxDepth = 3, CancellationToken ct = default);

    /// <summary>
    /// Finds the largest files under <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Root directory to scan.</param>
    /// <param name="count">Maximum number of files to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Files sorted by size descending, capped at <paramref name="count"/>.</returns>
    Task<List<LargeFileInfo>> GetLargestFilesAsync(string path, int count = 50, CancellationToken ct = default);
}

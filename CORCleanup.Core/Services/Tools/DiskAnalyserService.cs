using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Tools;

/// <summary>
/// Analyses disk space usage by recursively enumerating directories.
/// Provides folder-size tree data for treemap/drill-down visualisation
/// and largest-file lists for quick identification of space hogs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DiskAnalyserService : IDiskAnalyserService
{
    public Task<FolderSizeInfo> AnalyseFolderAsync(
        string path, int maxDepth = 3, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Directory not found: {path}");

            var root = BuildFolderTree(dir, currentDepth: 0, maxDepth, ct);
            CalculatePercentages(root);
            return root;
        }, ct);
    }

    public Task<List<LargeFileInfo>> GetLargestFilesAsync(
        string path, int count = 50, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Directory not found: {path}");

            // Use a sorted set capped at `count` to avoid holding millions of FileInfo in memory.
            // We keep the top N largest files using a min-heap approach via SortedSet.
            var topFiles = new SortedSet<(long Size, string FullName, DateTime LastWrite, string Extension)>(
                Comparer<(long Size, string FullName, DateTime LastWrite, string Extension)>
                    .Create((a, b) =>
                    {
                        int cmp = a.Size.CompareTo(b.Size);
                        return cmp != 0 ? cmp : string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase);
                    }));

            EnumerateLargestFiles(dir, topFiles, count, ct);

            var result = topFiles
                .Reverse()
                .Select(f => new LargeFileInfo
                {
                    FullPath = f.FullName,
                    Name = Path.GetFileName(f.FullName),
                    SizeBytes = f.Size,
                    LastModified = f.LastWrite,
                    Extension = f.Extension.ToLowerInvariant()
                })
                .ToList();

            return result;
        }, ct);
    }

    /// <summary>
    /// Recursively builds the folder tree, calculating sizes bottom-up.
    /// Access-denied directories are silently skipped.
    /// </summary>
    private static FolderSizeInfo BuildFolderTree(
        DirectoryInfo dir, int currentDepth, int maxDepth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var node = new FolderSizeInfo
        {
            Path = dir.FullName,
            Name = dir.Name,
            SizeBytes = 0
        };

        // Sum file sizes in this directory
        try
        {
            foreach (var file in dir.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    node.SizeBytes += file.Length;
                    node.FileCount++;
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        // Recurse into subdirectories if within depth limit
        try
        {
            foreach (var subDir in dir.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                node.FolderCount++;

                try
                {
                    if (currentDepth < maxDepth)
                    {
                        var child = BuildFolderTree(subDir, currentDepth + 1, maxDepth, ct);
                        node.Children.Add(child);
                        node.SizeBytes += child.SizeBytes;
                        node.FileCount += child.FileCount;
                        node.FolderCount += child.FolderCount;
                    }
                    else
                    {
                        // Beyond max depth: calculate size without building child tree
                        long deepSize = CalculateDirectorySize(subDir, ct);
                        node.SizeBytes += deepSize;
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        // Sort children by size descending for consistent treemap rendering
        node.Children.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));

        return node;
    }

    /// <summary>
    /// Calculates total size of a directory tree without building child nodes.
    /// Uses manual recursion instead of SearchOption.AllDirectories so that
    /// an access-denied subdirectory only loses THAT subtree, not the entire enumeration.
    /// </summary>
    private static long CalculateDirectorySize(DirectoryInfo dir, CancellationToken ct)
    {
        long total = 0;

        // Sum files in this directory
        try
        {
            foreach (var file in dir.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();
                try { total += file.Length; }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        // Recurse into each subdirectory independently
        try
        {
            foreach (var subDir in dir.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                try { total += CalculateDirectorySize(subDir, ct); }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        return total;
    }

    /// <summary>
    /// Sets Percentage on each child relative to its parent's SizeBytes.
    /// </summary>
    private static void CalculatePercentages(FolderSizeInfo node)
    {
        if (node.SizeBytes <= 0) return;

        foreach (var child in node.Children)
        {
            child.Percentage = node.SizeBytes > 0
                ? (double)child.SizeBytes / node.SizeBytes * 100.0
                : 0;
            CalculatePercentages(child);
        }
    }

    /// <summary>
    /// Enumerates all files recursively, maintaining a bounded set of the largest N.
    /// Uses manual recursion instead of SearchOption.AllDirectories so that
    /// an access-denied subdirectory only loses THAT subtree, not the entire enumeration.
    /// </summary>
    private static void EnumerateLargestFiles(
        DirectoryInfo dir,
        SortedSet<(long Size, string FullName, DateTime LastWrite, string Extension)> topFiles,
        int count,
        CancellationToken ct)
    {
        // Process files in this directory
        try
        {
            foreach (var file in dir.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    long size = file.Length;

                    if (topFiles.Count >= count && size <= topFiles.Min.Size)
                        continue;

                    topFiles.Add((size, file.FullName, file.LastWriteTime, file.Extension));

                    if (topFiles.Count > count)
                        topFiles.Remove(topFiles.Min);
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        // Recurse into each subdirectory independently
        try
        {
            foreach (var subDir in dir.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                try { EnumerateLargestFiles(subDir, topFiles, count, ct); }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }
}

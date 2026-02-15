using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using RecycleBin = Microsoft.VisualBasic.FileIO.FileSystem;

namespace CORCleanup.Core.Services.Tools;

/// <summary>
/// Finds duplicate files using a multi-stage algorithm optimised for speed:
///   Stage 1 — Group by file size (eliminates ~95% of candidates instantly).
///   Stage 2 — SHA-256 hash of first 4 KB (eliminates most remaining false matches).
///   Stage 3 — Full SHA-256 hash (confirms true duplicates).
/// Results are yielded as confirmed groups via IAsyncEnumerable for progressive UI updates.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DuplicateFinderService : IDuplicateFinderService
{
    private const int PartialHashBytes = 4096; // 4 KB
    private const int BufferSize = 81920;      // 80 KB — matches .NET FileStream default

    public async IAsyncEnumerable<DuplicateGroup> FindDuplicatesAsync(
        string path, long minSizeBytes = 1024,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        // ── Stage 1: Group by file size ────────────────────────────────────
        var sizeGroups = new Dictionary<long, List<FileInfo>>();

        IEnumerable<FileInfo> allFiles;
        try
        {
            allFiles = dir.EnumerateFiles("*", SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in allFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (file.Length < minSizeBytes) continue;

                if (!sizeGroups.TryGetValue(file.Length, out var group))
                {
                    group = new List<FileInfo>();
                    sizeGroups[file.Length] = group;
                }
                group.Add(file);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        // ── Stage 2 & 3: Hash candidates ──────────────────────────────────
        foreach (var (fileSize, files) in sizeGroups)
        {
            ct.ThrowIfCancellationRequested();

            // Only files that share a size are candidates
            if (files.Count < 2) continue;

            // Stage 2: Partial hash (first 4 KB)
            var partialGroups = new Dictionary<string, List<FileInfo>>(StringComparer.Ordinal);

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    string partialHash = await ComputePartialHashAsync(file.FullName, ct);
                    if (!partialGroups.TryGetValue(partialHash, out var pGroup))
                    {
                        pGroup = new List<FileInfo>();
                        partialGroups[partialHash] = pGroup;
                    }
                    pGroup.Add(file);
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }

            // Stage 3: Full hash only for files with matching partial hashes
            foreach (var (_, partialMatches) in partialGroups)
            {
                ct.ThrowIfCancellationRequested();

                if (partialMatches.Count < 2) continue;

                var fullGroups = new Dictionary<string, List<FileInfo>>(StringComparer.Ordinal);

                foreach (var file in partialMatches)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        string fullHash = await ComputeFullHashAsync(file.FullName, ct);
                        if (!fullGroups.TryGetValue(fullHash, out var fGroup))
                        {
                            fGroup = new List<FileInfo>();
                            fullGroups[fullHash] = fGroup;
                        }
                        fGroup.Add(file);
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }

                // Yield confirmed duplicate groups
                foreach (var (hash, dupes) in fullGroups)
                {
                    if (dupes.Count < 2) continue;

                    yield return new DuplicateGroup
                    {
                        Hash = hash,
                        FileSize = fileSize,
                        Files = dupes.Select(f => new DuplicateFile
                        {
                            FullPath = f.FullName,
                            Name = f.Name,
                            SizeBytes = f.Length,
                            LastModified = f.LastWriteTime
                        }).ToList()
                    };
                }
            }
        }
    }

    public Task DeleteToRecycleBinAsync(string filePath)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found.", filePath);

            RecycleBin.DeleteFile(
                filePath,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        });
    }

    /// <summary>
    /// SHA-256 hash of only the first <see cref="PartialHashBytes"/> bytes.
    /// For files smaller than 4 KB, this is effectively a full hash.
    /// </summary>
    private static async Task<string> ComputePartialHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            PartialHashBytes, FileOptions.SequentialScan | FileOptions.Asynchronous);

        var buffer = new byte[PartialHashBytes];
        int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, PartialHashBytes), ct);

        return Convert.ToHexString(
            SHA256.HashData(buffer.AsSpan(0, bytesRead))).ToLowerInvariant();
    }

    /// <summary>
    /// Full SHA-256 hash of the entire file using incremental hashing and an 80 KB buffer.
    /// </summary>
    private static async Task<string> ComputeFullHashAsync(string filePath, CancellationToken ct)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous);

        var buffer = new byte[BufferSize];
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
        {
            hasher.AppendData(buffer.AsSpan(0, bytesRead));
        }

        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }
}

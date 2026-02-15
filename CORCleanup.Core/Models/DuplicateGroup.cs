namespace CORCleanup.Core.Models;

/// <summary>
/// Represents a single file within a group of duplicates.
/// </summary>
public sealed class DuplicateFile
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime LastModified { get; init; }

    /// <summary>
    /// UI binding property — user selects which duplicates to delete.
    /// </summary>
    public bool IsSelected { get; set; }

    public string SizeFormatted => ByteFormatter.Format(SizeBytes);
    public string LastModifiedFormatted => LastModified.ToString("dd/MM/yyyy HH:mm");
    public string Directory => System.IO.Path.GetDirectoryName(FullPath) ?? "";
}

/// <summary>
/// A group of files that are byte-identical duplicates.
/// Hash is the SHA-256 digest of the file contents.
/// </summary>
public sealed class DuplicateGroup
{
    public required string Hash { get; init; }
    public required long FileSize { get; init; }
    public required List<DuplicateFile> Files { get; init; }

    public int Count => Files.Count;

    /// <summary>
    /// Total wasted space: (copies - 1) * file size.
    /// </summary>
    public long WastedBytes => FileSize * (Count - 1);

    public string FileSizeFormatted => ByteFormatter.Format(FileSize);
    public string WastedFormatted => ByteFormatter.Format(WastedBytes);
}

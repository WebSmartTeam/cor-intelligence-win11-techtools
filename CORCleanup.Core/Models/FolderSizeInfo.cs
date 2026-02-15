namespace CORCleanup.Core.Models;

/// <summary>
/// Represents a folder's size breakdown for disk space analysis.
/// Supports recursive children for treemap/drill-down visualisation.
/// </summary>
public sealed class FolderSizeInfo
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public int FolderCount { get; set; }
    public List<FolderSizeInfo> Children { get; set; } = new();

    /// <summary>
    /// Percentage of parent folder's total size (0-100).
    /// Calculated after full enumeration.
    /// </summary>
    public double Percentage { get; set; }

    public string SizeFormatted => ByteFormatter.Format(SizeBytes);
}

/// <summary>
/// Represents a single large file found during disk analysis.
/// </summary>
public sealed class LargeFileInfo
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime LastModified { get; init; }
    public required string Extension { get; init; }

    public string SizeFormatted => ByteFormatter.Format(SizeBytes);
    public string LastModifiedFormatted => LastModified.ToString("dd/MM/yyyy HH:mm");
}

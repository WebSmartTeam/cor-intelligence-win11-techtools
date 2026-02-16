namespace CORCleanup.Core.Models;

/// <summary>
/// Logical drive/volume from Win32_LogicalDisk — drive letter, file system, capacity.
/// </summary>
public sealed class LogicalVolumeInfo
{
    public required string DriveLetter { get; init; }
    public required string VolumeLabel { get; init; }
    public required string FileSystem { get; init; }
    public required long SizeBytes { get; init; }
    public required long FreeSpaceBytes { get; init; }
    public required int DriveType { get; init; }

    public long UsedSpaceBytes => SizeBytes - FreeSpaceBytes;
    public double UsedPercent => SizeBytes > 0 ? (double)UsedSpaceBytes / SizeBytes * 100 : 0;

    public string SizeFormatted => ByteFormatter.Format(SizeBytes);
    public string FreeFormatted => ByteFormatter.Format(FreeSpaceBytes);
    public string UsedFormatted => ByteFormatter.Format(UsedSpaceBytes);
    public string UsedPercentFormatted => $"{UsedPercent:F0}%";

    public string DisplayLabel => string.IsNullOrWhiteSpace(VolumeLabel)
        ? DriveLetter
        : $"{DriveLetter} ({VolumeLabel})";

    public string DriveTypeDisplay => DriveType switch
    {
        2 => "Removable",
        3 => "Fixed",
        4 => "Network",
        5 => "CD-ROM",
        6 => "RAM Disk",
        _ => "Unknown"
    };
}

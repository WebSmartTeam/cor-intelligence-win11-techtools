namespace CORCleanup.Core.Interfaces;

/// <summary>
/// Overwrite pattern for secure file deletion.
/// Modern drives: 1-3 passes is sufficient (35-pass Gutmann is obsolete for flash/SSD).
/// </summary>
public enum ShredMethod
{
    /// <summary>Single pass of 0x00 bytes. Fast, sufficient for most use cases.</summary>
    ZeroFill,

    /// <summary>US DoD 5220.22-M: Pass 1 = 0x00, Pass 2 = 0xFF, Pass 3 = random bytes.</summary>
    DoD3Pass,

    /// <summary>7 alternating overwrite patterns for enhanced assurance.</summary>
    Enhanced7Pass
}

public interface IFileShredderService
{
    /// <summary>
    /// Securely overwrites and deletes a single file.
    /// After overwriting, the file is renamed to a random name then deleted.
    /// </summary>
    Task ShredFileAsync(string filePath, ShredMethod method = ShredMethod.ZeroFill,
        IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Securely overwrites and deletes multiple files.
    /// Progress reports 0.0 to 1.0 across the entire batch.
    /// </summary>
    Task ShredFilesAsync(IEnumerable<string> filePaths, ShredMethod method = ShredMethod.ZeroFill,
        IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Overwrites free space on a drive by creating and filling a temporary file.
    /// Essential for MSP data destruction on decommissioned drives.
    /// </summary>
    /// <param name="driveLetter">Drive root, e.g. "C:\".</param>
    Task WipeFreeSpaceAsync(string driveLetter, ShredMethod method = ShredMethod.ZeroFill,
        IProgress<double>? progress = null, CancellationToken ct = default);
}

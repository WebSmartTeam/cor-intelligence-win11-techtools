using System.Runtime.Versioning;
using System.Security.Cryptography;
using CORCleanup.Core.Interfaces;

namespace CORCleanup.Core.Services.Tools;

/// <summary>
/// Secure file deletion service that overwrites file contents before removal.
/// After overwriting, the file is renamed to a random name and then deleted,
/// preventing recovery via file-name metadata.
///
/// Modern drives: 1-3 passes is sufficient. 35-pass Gutmann is obsolete
/// for flash/SSD storage and provides no additional security benefit.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FileShredderService : IFileShredderService
{
    private const int BufferSize = 65536; // 64 KB write buffer

    /// <summary>
    /// Overwrite patterns per shred method. Each byte[] is one full pass over the file.
    /// A null entry means "fill with cryptographically random bytes".
    /// </summary>
    private static readonly Dictionary<ShredMethod, byte?[]> PassPatterns = new()
    {
        [ShredMethod.ZeroFill] = [0x00],
        [ShredMethod.DoD3Pass] = [0x00, 0xFF, null],
        [ShredMethod.Enhanced7Pass] = [0x00, 0xFF, null, 0x55, 0xAA, null, 0x00]
    };

    public async Task ShredFileAsync(
        string filePath, ShredMethod method = ShredMethod.ZeroFill,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found.", filePath);

        var fileInfo = new FileInfo(filePath);

        // Remove read-only attribute if set, otherwise overwrite will fail
        if (fileInfo.IsReadOnly)
            fileInfo.IsReadOnly = false;

        long fileLength = fileInfo.Length;
        var patterns = PassPatterns[method];
        int totalPasses = patterns.Length;

        for (int pass = 0; pass < totalPasses; pass++)
        {
            ct.ThrowIfCancellationRequested();

            byte? patternByte = patterns[pass];
            var buffer = new byte[BufferSize];

            if (patternByte.HasValue)
            {
                // Fill buffer with the fixed pattern byte
                Array.Fill(buffer, patternByte.Value);
            }
            else
            {
                // Random pass — initial fill; re-randomised per chunk below
                RandomNumberGenerator.Fill(buffer);
            }

            await using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Write, FileShare.None,
                BufferSize, FileOptions.WriteThrough | FileOptions.Asynchronous);

            long bytesWritten = 0;

            while (bytesWritten < fileLength)
            {
                ct.ThrowIfCancellationRequested();

                int chunkSize = (int)Math.Min(BufferSize, fileLength - bytesWritten);

                // Re-randomise buffer for random passes to avoid writing the same block repeatedly
                if (!patternByte.HasValue)
                    RandomNumberGenerator.Fill(buffer.AsSpan(0, chunkSize));

                await stream.WriteAsync(buffer.AsMemory(0, chunkSize), ct);
                bytesWritten += chunkSize;

                // Progress: fraction of total work across all passes
                double fraction = ((double)pass + (double)bytesWritten / Math.Max(fileLength, 1)) / totalPasses;
                progress?.Report(Math.Min(fraction, 1.0));
            }

            // Flush to physical media before next pass
            await stream.FlushAsync(ct);
        }

        // Rename to random name before deletion to destroy filename metadata
        string directory = Path.GetDirectoryName(filePath)!;
        string randomName = Path.Combine(directory, Path.GetRandomFileName());

        try
        {
            File.Move(filePath, randomName);
            File.Delete(randomName);
        }
        catch
        {
            // Fallback: delete with original name if rename fails (e.g. permissions)
            File.Delete(filePath);
        }

        progress?.Report(1.0);
    }

    public async Task ShredFilesAsync(
        IEnumerable<string> filePaths, ShredMethod method = ShredMethod.ZeroFill,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var paths = filePaths.ToList();
        if (paths.Count == 0) return;

        for (int i = 0; i < paths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            int fileIndex = i; // capture for closure
            var fileProgress = progress is not null
                ? new Progress<double>(p =>
                {
                    // Scale individual file progress into overall batch progress
                    double overall = (fileIndex + p) / paths.Count;
                    progress.Report(Math.Min(overall, 1.0));
                })
                : null;

            await ShredFileAsync(paths[i], method, fileProgress, ct);
        }

        progress?.Report(1.0);
    }

    public async Task WipeFreeSpaceAsync(
        string driveLetter, ShredMethod method = ShredMethod.ZeroFill,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        // Normalise drive path (accept "C", "C:", or "C:\")
        if (driveLetter.Length == 1)
            driveLetter += @":\";
        else if (driveLetter.Length == 2 && driveLetter[1] == ':')
            driveLetter += @"\";

        var driveInfo = new DriveInfo(driveLetter);
        if (!driveInfo.IsReady)
            throw new IOException($"Drive {driveLetter} is not ready.");

        var patterns = PassPatterns[method];
        int totalPasses = patterns.Length;

        for (int pass = 0; pass < totalPasses; pass++)
        {
            ct.ThrowIfCancellationRequested();

            byte? patternByte = patterns[pass];
            var buffer = new byte[BufferSize];

            if (patternByte.HasValue)
                Array.Fill(buffer, patternByte.Value);
            else
                RandomNumberGenerator.Fill(buffer);

            // Create a temp file and fill it until the disk is full
            string tempPath = Path.Combine(driveLetter, $".cor_wipe_{Guid.NewGuid():N}.tmp");

            try
            {
                await using var stream = new FileStream(
                    tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    BufferSize, FileOptions.WriteThrough | FileOptions.Asynchronous);

                long totalFreeAtStart = driveInfo.AvailableFreeSpace;
                long bytesWritten = 0;

                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        if (!patternByte.HasValue)
                            RandomNumberGenerator.Fill(buffer);

                        await stream.WriteAsync(buffer.AsMemory(), ct);
                        bytesWritten += BufferSize;

                        // Report progress for this pass
                        if (totalFreeAtStart > 0)
                        {
                            double fraction = ((double)pass +
                                Math.Min((double)bytesWritten / totalFreeAtStart, 1.0)) / totalPasses;
                            progress?.Report(Math.Min(fraction, 1.0));
                        }
                    }
                    catch (IOException)
                    {
                        // Disk full — expected, this is the goal
                        break;
                    }
                }

                await stream.FlushAsync(ct);
            }
            finally
            {
                // Always clean up the temp file
                try { File.Delete(tempPath); } catch { }
            }
        }

        progress?.Report(1.0);
    }
}

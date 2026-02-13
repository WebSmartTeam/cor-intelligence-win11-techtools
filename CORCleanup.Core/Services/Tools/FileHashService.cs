using System.Security.Cryptography;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Tools;

public sealed class FileHashService : IFileHashService
{
    private const int BufferSize = 81920; // 80 KB — matches .NET default for FileStream

    public async Task<FileHashResult> ComputeHashesAsync(
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("File not found.", filePath);

        var totalBytes = fileInfo.Length;
        long processedBytes = 0;

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous);

        var buffer = new byte[BufferSize];
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
        {
            md5.AppendData(buffer.AsSpan(0, bytesRead));
            sha1.AppendData(buffer.AsSpan(0, bytesRead));
            sha256.AppendData(buffer.AsSpan(0, bytesRead));

            processedBytes += bytesRead;
            if (totalBytes > 0)
                progress?.Report((double)processedBytes / totalBytes * 100);
        }

        return new FileHashResult
        {
            FilePath = filePath,
            FileName = fileInfo.Name,
            FileSizeBytes = totalBytes,
            Md5 = Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant(),
            Sha1 = Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant(),
            Sha256 = Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant()
        };
    }
}

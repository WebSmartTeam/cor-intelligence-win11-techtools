using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IFileHashService
{
    Task<FileHashResult> ComputeHashesAsync(
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}

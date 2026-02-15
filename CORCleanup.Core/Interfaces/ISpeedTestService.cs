using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface ISpeedTestService
{
    Task<SpeedTestResult> RunTestAsync(
        IProgress<SpeedTestProgress>? progress = null,
        CancellationToken ct = default);
}

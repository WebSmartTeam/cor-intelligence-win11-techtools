using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface ISystemRepairService
{
    Task<SystemRepairResult> RunSfcScanAsync(IProgress<string>? progress = null, CancellationToken ct = default);
    Task<SystemRepairResult> RunDismRestoreHealthAsync(IProgress<string>? progress = null, CancellationToken ct = default);
    Task<SystemRepairResult> ResetNetworkStackAsync(IProgress<string>? progress = null, CancellationToken ct = default);
    Task<SystemRepairResult> FlushDnsAsync(CancellationToken ct = default);
    Task<SystemRepairResult> ResetWindowsUpdateAsync(IProgress<string>? progress = null, CancellationToken ct = default);
}

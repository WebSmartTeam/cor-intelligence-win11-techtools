using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IDebloatService
{
    /// <summary>
    /// Returns the known bloatware list with installation status checked against the current system.
    /// </summary>
    Task<List<AppxPackageInfo>> GetBloatwareListAsync();

    /// <summary>
    /// Removes the specified AppX packages for all users and deprovisions them to prevent reinstallation.
    /// </summary>
    Task<DebloatResult> RemovePackagesAsync(IEnumerable<AppxPackageInfo> packages, IProgress<string>? progress = null);

    /// <summary>
    /// Disables Microsoft Copilot via registry policies and removes the AppX package.
    /// </summary>
    Task<bool> DisableCopilotAsync(IProgress<string>? progress = null);

    /// <summary>
    /// Disables Windows Recall via registry policies (24H2 Copilot+ PCs).
    /// </summary>
    Task<bool> DisableRecallAsync(IProgress<string>? progress = null);

    /// <summary>
    /// Applies privacy-focused registry tweaks: disables telemetry, advertising ID,
    /// Start menu suggestions, Bing search, and lock screen ads.
    /// </summary>
    Task<bool> ApplyPrivacyTweaksAsync(IProgress<string>? progress = null);
}

using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

/// <summary>
/// Service for scanning, fixing, and managing registry issues.
/// All fix operations create a .reg backup before making changes.
/// </summary>
public interface IRegistryCleanerService
{
    /// <summary>
    /// Scans the registry for issues across all categories.
    /// </summary>
    Task<List<RegistryIssue>> ScanAsync(
        IProgress<(RegistryScanCategory Category, int Found)>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a .reg backup of the specified registry keys, then deletes them.
    /// Returns the fix result including backup file path.
    /// </summary>
    Task<RegistryFixResult> FixSelectedAsync(
        List<RegistryIssue> issues,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a .reg backup file by importing it.
    /// </summary>
    Task<bool> RestoreBackupAsync(string backupFilePath);

    /// <summary>
    /// Returns all existing backup files sorted by date (newest first).
    /// </summary>
    Task<List<RegistryBackup>> GetBackupsAsync();

    /// <summary>
    /// Deletes a backup .reg file from disk.
    /// </summary>
    Task<bool> DeleteBackupAsync(string backupFilePath);

    /// <summary>
    /// Gets the backup directory path (%APPDATA%\COR Cleanup\Backups\).
    /// </summary>
    string GetBackupDirectory();
}

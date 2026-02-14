using System.ComponentModel;

namespace CORCleanup.Core.Models;

/// <summary>
/// Categories of registry issues the cleaner can detect.
/// </summary>
public enum RegistryScanCategory
{
    MissingSharedDlls,
    UnusedFileExtensions,
    OrphanedComActiveX,
    InvalidApplicationPaths,
    ObsoleteSoftwareEntries,
    MissingMuiReferences,
    InvalidFirewallRules,
    StaleInstallerReferences,
    DeadShortcutReferences
}

/// <summary>
/// Risk level for a detected registry issue.
/// Green = safe to fix, Amber = review first, Red = proceed with caution.
/// </summary>
public enum RegistryRiskLevel
{
    Safe,    // Green — low risk, safe to remove
    Review,  // Amber — should inspect before removing
    Caution  // Red — could affect system if wrong
}

/// <summary>
/// A single registry issue found during scanning.
/// Implements INotifyPropertyChanged so DataGrid checkbox bindings
/// update when IsSelected is changed programmatically (e.g. Select All).
/// </summary>
public sealed class RegistryIssue : INotifyPropertyChanged
{
    public required RegistryScanCategory Category { get; init; }
    public required RegistryRiskLevel Risk { get; init; }
    public required string KeyPath { get; init; }
    public string? ValueName { get; init; }
    public required string Description { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CategoryDisplayName => Category switch
    {
        RegistryScanCategory.MissingSharedDlls => "Missing Shared DLLs",
        RegistryScanCategory.UnusedFileExtensions => "Unused File Extensions",
        RegistryScanCategory.OrphanedComActiveX => "Orphaned COM/ActiveX",
        RegistryScanCategory.InvalidApplicationPaths => "Invalid Application Paths",
        RegistryScanCategory.ObsoleteSoftwareEntries => "Obsolete Software Entries",
        RegistryScanCategory.MissingMuiReferences => "Missing MUI References",
        RegistryScanCategory.InvalidFirewallRules => "Invalid Firewall Rules",
        RegistryScanCategory.StaleInstallerReferences => "Stale Installer References",
        RegistryScanCategory.DeadShortcutReferences => "Dead Shortcut References",
        _ => Category.ToString()
    };

    public string RiskDisplayName => Risk switch
    {
        RegistryRiskLevel.Safe => "Safe",
        RegistryRiskLevel.Review => "Review",
        RegistryRiskLevel.Caution => "Caution",
        _ => Risk.ToString()
    };
}

/// <summary>
/// A registry backup record for the backup management UI.
/// </summary>
public sealed class RegistryBackup
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required long FileSizeBytes { get; init; }
    public required int IssueCount { get; init; }

    public string CreatedFormatted => CreatedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string SizeFormatted => ByteFormatter.Format(FileSizeBytes);
}

/// <summary>
/// Result of a registry fix operation.
/// </summary>
public sealed class RegistryFixResult
{
    public required int TotalSelected { get; init; }
    public required int Fixed { get; init; }
    public required int Failed { get; init; }
    public required string BackupFilePath { get; init; }
    public List<string> Errors { get; init; } = new();

    /// <summary>
    /// Key paths of issues that were successfully fixed.
    /// Used by the ViewModel to remove the correct items from the UI.
    /// </summary>
    public HashSet<string> FixedKeyPaths { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

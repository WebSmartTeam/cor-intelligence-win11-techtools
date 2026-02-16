namespace CORCleanup.Core.Models;

/// <summary>
/// How the programme was installed — detected from registry values.
/// </summary>
public enum InstallSource
{
    Unknown,
    Msi,
    Exe,
    StoreApp,
}

/// <summary>
/// Category of leftover item found after uninstall.
/// </summary>
public enum LeftoverType
{
    RegistryKey,
    File,
    Folder,
}

/// <summary>
/// Risk rating for leftover removal — guides user decision.
/// </summary>
public enum LeftoverConfidence
{
    /// <summary>Definitely this programme's data, safe to remove.</summary>
    Safe,
    /// <summary>Likely this programme's, but review before removing.</summary>
    Review,
    /// <summary>May be shared with other programmes — use caution.</summary>
    Caution,
}

/// <summary>
/// A leftover item (file, folder, or registry key) found after uninstall.
/// Includes confidence scoring to guide the user's removal decision.
/// </summary>
public sealed class UninstallLeftover
{
    public required string Path { get; init; }
    public required LeftoverType Type { get; init; }
    public required LeftoverConfidence Confidence { get; init; }
    public long? SizeBytes { get; init; }
    public string Description { get; init; } = "";

    /// <summary>
    /// Pre-selected for removal. Caution items default to false.
    /// User toggles via checkbox in the leftover DataGrid.
    /// </summary>
    public bool IsSelected { get; set; } = true;

    public string SizeFormatted =>
        SizeBytes.HasValue ? ByteFormatter.Format(SizeBytes.Value) : "";

    public string ConfidenceLabel => Confidence switch
    {
        LeftoverConfidence.Safe => "Safe to remove",
        LeftoverConfidence.Review => "Review recommended",
        LeftoverConfidence.Caution => "Use caution",
        _ => ""
    };

    public string TypeLabel => Type switch
    {
        LeftoverType.RegistryKey => "Registry",
        LeftoverType.File => "File",
        LeftoverType.Folder => "Folder",
        _ => ""
    };
}

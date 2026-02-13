namespace CORCleanup.Core;

/// <summary>
/// Centralised byte/size formatting utility.
/// Replaces duplicated FormatBytes implementations across model classes.
/// </summary>
public static class ByteFormatter
{
    /// <summary>
    /// Formats a byte count into a human-readable string (B, KB, MB, GB).
    /// </summary>
    public static string Format(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    /// <summary>
    /// Formats a kilobyte count into a human-readable string (KB, MB, GB).
    /// Used by registry-sourced EstimatedSize values which are stored in KB.
    /// </summary>
    public static string FormatFromKb(long kilobytes) => kilobytes switch
    {
        0 => "",
        < 1024 => $"{kilobytes} KB",
        < 1024 * 1024 => $"{kilobytes / 1024.0:F1} MB",
        _ => $"{kilobytes / (1024.0 * 1024):F1} GB"
    };
}

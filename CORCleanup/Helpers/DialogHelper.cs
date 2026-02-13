using System.Windows;

namespace CORCleanup.Helpers;

/// <summary>
/// Lightweight confirmation dialogs for destructive operations.
/// </summary>
public static class DialogHelper
{
    /// <summary>
    /// Shows a Yes/No confirmation dialog. Returns true if the user clicks Yes.
    /// </summary>
    public static bool Confirm(string message, string title = "COR Cleanup — Confirm")
    {
        return MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}

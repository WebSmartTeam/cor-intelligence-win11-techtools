namespace CORCleanup.Core.Models;

/// <summary>
/// Detected Windows 11 edition. Used to gate Pro-only features
/// (BitLocker, Group Policy, Hyper-V, Remote Desktop).
/// Registry: HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\EditionID
/// "Professional" = Pro, "Core" = Home.
/// </summary>
public enum WindowsEdition
{
    Unknown,
    Home,
    Pro
}

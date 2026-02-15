using System.Management;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;

namespace CORCleanup.Core.Services.Hardware;

/// <summary>
/// Recovers the Windows product key and activation status using three methods:
///
/// 1. OEM Key: Queries SoftwareLicensingService for OA3xOriginalProductKey — the
///    BIOS/UEFI-embedded key injected by the OEM during manufacturing. Present on
///    all pre-installed Windows machines (Dell, HP, Lenovo, etc.).
///
/// 2. Registry Key: Reads HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\DigitalProductId
///    and decodes the base-24 encoded product key from bytes 52-66. This is the
///    retail/volume key entered during installation.
///
/// 3. Activation Status: Queries SoftwareLicensingProduct for LicenseStatus on
///    the active Windows licence, returning a human-readable activation state.
///
/// Both key methods may return null on clean/BYOL installs or digital-licence-only
/// activations (common on Win11 upgrades where the key is linked to Microsoft account).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProductKeyService : IProductKeyService
{
    public Task<string?> GetOemKeyAsync() => Task.Run(() =>
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT OA3xOriginalProductKey FROM SoftwareLicensingService");

            foreach (var obj in searcher.Get())
            {
                var key = obj["OA3xOriginalProductKey"]?.ToString();
                if (!string.IsNullOrWhiteSpace(key))
                    return key;
            }
        }
        catch (ManagementException)
        {
            // Not available on all systems (VMs, custom builds, etc.)
        }

        return (string?)null;
    });

    public Task<string?> GetRegistryKeyAsync() => Task.Run(() =>
    {
        try
        {
            using var regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

            var digitalProductId = regKey?.GetValue("DigitalProductId") as byte[];

            if (digitalProductId is not { Length: >= 67 })
                return (string?)null;

            return DecodeProductKey(digitalProductId);
        }
        catch
        {
            // Registry access may fail without elevation or on non-standard installs
            return (string?)null;
        }
    });

    public Task<string> GetActivationStatusAsync() => Task.Run(() =>
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT LicenseStatus, Name, Description FROM SoftwareLicensingProduct " +
                "WHERE Name LIKE 'Windows%' AND LicenseStatus > 0");

            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";

                // Only interested in Windows OS licences, not embedded components
                if (!name.Contains("Windows", StringComparison.OrdinalIgnoreCase))
                    continue;

                var status = obj["LicenseStatus"];
                if (status is null) continue;

                var licenseStatus = Convert.ToInt32(status);
                return FormatLicenseStatus(licenseStatus);
            }

            // If no matching product found with status > 0, check for unlicensed
            using var allSearcher = new ManagementObjectSearcher(
                "SELECT LicenseStatus FROM SoftwareLicensingProduct " +
                "WHERE Name LIKE 'Windows%' AND ApplicationId = '55c92734-d682-4d71-983e-d6ec3f16059f'");

            foreach (var obj in allSearcher.Get())
            {
                var status = obj["LicenseStatus"];
                if (status is not null)
                    return FormatLicenseStatus(Convert.ToInt32(status));
            }
        }
        catch (ManagementException)
        {
            // WMI query failure
        }

        return "Unknown";
    });

    // --- Product key decoding ---

    /// <summary>
    /// Decodes the Windows product key from the DigitalProductId registry value.
    /// The key is base-24 encoded in bytes 52-66 of the DigitalProductId byte array.
    /// Win8+ uses a modified encoding with an 'N' insertion character.
    /// </summary>
    private static string DecodeProductKey(byte[] digitalProductId)
    {
        const string chars = "BCDFGHJKMPQRTVWXY2346789";
        const int keyOffset = 52;

        // Win8+ detection: bit at byte 66 indicates new encoding scheme
        var isWin8Plus = (digitalProductId[66] / 6) & 1;
        digitalProductId[66] = (byte)((digitalProductId[66] & 0xF7) | ((isWin8Plus & 2) * 4));

        var decoded = new char[25];
        var last = 0;

        for (var i = 24; i >= 0; i--)
        {
            var current = 0;
            for (var j = 14; j >= 0; j--)
            {
                current *= 256;
                current += digitalProductId[j + keyOffset];
                digitalProductId[j + keyOffset] = (byte)(current / 24);
                current %= 24;
            }
            decoded[i] = chars[current];
            last = current;
        }

        var keyString = new string(decoded);

        // Win8+ keys have an 'N' inserted at the position indicated by 'last'
        if (isWin8Plus != 0)
        {
            keyString = keyString.Insert(last, "N");
            keyString = keyString[..25]; // Ensure exactly 25 characters
        }

        // Format as XXXXX-XXXXX-XXXXX-XXXXX-XXXXX
        return $"{keyString[..5]}-{keyString[5..10]}-{keyString[10..15]}-{keyString[15..20]}-{keyString[20..25]}";
    }

    // --- Licence status formatting ---

    /// <summary>
    /// Maps SoftwareLicensingProduct.LicenseStatus integer to human-readable text.
    /// https://learn.microsoft.com/en-us/previous-versions/windows/desktop/sppwmi/softwarelicensingproduct
    /// </summary>
    private static string FormatLicenseStatus(int status) => status switch
    {
        0 => "Unlicensed",
        1 => "Licensed (Activated)",
        2 => "Out-of-Box Grace Period",
        3 => "Out-of-Tolerance Grace Period",
        4 => "Non-Genuine Grace Period",
        5 => "Notification Mode",
        6 => "Extended Grace Period",
        _ => $"Unknown Status ({status})"
    };
}

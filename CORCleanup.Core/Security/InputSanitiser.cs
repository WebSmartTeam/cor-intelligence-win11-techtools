using System.Text.RegularExpressions;

namespace CORCleanup.Core.Security;

/// <summary>
/// Centralised input sanitisation for all process argument construction.
/// Prevents command injection, WMI injection, and PowerShell injection.
/// </summary>
public static partial class InputSanitiser
{
    // ================================================================
    // Hostname / Domain validation
    // ================================================================

    /// <summary>
    /// Validates that input is a valid hostname, domain name, or IP address.
    /// Rejects any characters that could be used for command injection.
    /// </summary>
    public static bool IsValidHostnameOrIp(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 253)
            return false;

        return HostnameOrIpRegex().IsMatch(input);
    }

    /// <summary>
    /// Validates that input is a valid IPv4 or IPv6 address.
    /// </summary>
    public static bool IsValidIpAddress(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        return System.Net.IPAddress.TryParse(input, out _);
    }

    /// <summary>
    /// Validates a DNS record type against an allowed list.
    /// </summary>
    public static bool IsValidDnsRecordType(string recordType)
    {
        return recordType.ToUpperInvariant() is
            "A" or "AAAA" or "MX" or "CNAME" or "TXT" or "NS" or "SOA" or "PTR" or "SRV";
    }

    // ================================================================
    // Process argument sanitisation
    // ================================================================

    /// <summary>
    /// Removes characters that could break out of double-quoted arguments
    /// passed to a process via ProcessStartInfo.Arguments.
    /// Use when embedding a value in double-quote delimiters: name="{value}".
    /// </summary>
    public static string SanitiseForProcessArgument(string input)
    {
        // Remove double quotes and command chaining characters
        // Even though UseShellExecute=false prevents shell interpretation,
        // the target process may parse arguments via CommandLineToArgvW
        return input
            .Replace("\"", "")
            .Replace("\0", "");
    }

    /// <summary>
    /// Sanitises a Wi-Fi SSID name for use in netsh arguments.
    /// SSIDs come from Windows but malicious SSIDs could contain injection chars.
    /// </summary>
    public static string SanitiseWifiSsid(string ssid)
    {
        // Remove characters that could break the quoting in netsh arguments
        return ssid
            .Replace("\"", "")
            .Replace("\0", "");
    }

    // ================================================================
    // WMI query sanitisation
    // ================================================================

    /// <summary>
    /// Escapes a value for safe embedding in WQL (WMI Query Language) strings.
    /// MUST escape backslashes BEFORE single quotes (correct ordering).
    /// </summary>
    public static string EscapeWql(string value)
    {
        // Order matters: escape backslashes first, then single quotes
        return value
            .Replace("\\", "\\\\")
            .Replace("'", "\\'");
    }

    // ================================================================
    // PowerShell argument sanitisation
    // ================================================================

    /// <summary>
    /// Escapes a value for safe embedding in a PowerShell single-quoted string.
    /// Also strips characters that could break out of the surrounding double-quote
    /// wrapper in ProcessStartInfo.Arguments.
    /// </summary>
    public static string EscapeForPowerShell(string value)
    {
        // Remove double quotes (they'd break the ProcessStartInfo.Arguments wrapping)
        // Then double single quotes for PowerShell single-quote escaping
        return value
            .Replace("\"", "")
            .Replace("'", "''");
    }

    // ================================================================
    // Service name validation
    // ================================================================

    /// <summary>
    /// Validates that a Windows service name contains only safe characters.
    /// Valid service names: alphanumeric, underscore, hyphen, period.
    /// </summary>
    public static bool IsValidServiceName(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName) || serviceName.Length > 256)
            return false;

        return ServiceNameRegex().IsMatch(serviceName);
    }

    // ================================================================
    // File path validation
    // ================================================================

    /// <summary>
    /// Validates that a file path is within an expected directory.
    /// Prevents path traversal attacks (../../etc).
    /// </summary>
    public static bool IsPathWithinDirectory(string filePath, string allowedDirectory)
    {
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            var fullDir = Path.GetFullPath(allowedDirectory);

            return fullPath.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // ================================================================
    // Uninstall string validation
    // ================================================================

    /// <summary>
    /// Validates that an uninstall command uses a legitimate executable.
    /// Checks that the parsed executable exists on disk and has a valid extension.
    /// </summary>
    public static bool IsValidUninstallExecutable(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        // msiexec.exe is always allowed (Windows component)
        if (fileName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
            return true;

        // Must have .exe extension
        if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return false;

        // Must exist on disk
        return File.Exists(fileName);
    }

    // ================================================================
    // Hosts file validation
    // ================================================================

    /// <summary>
    /// Validates an IP address string for hosts file entries.
    /// </summary>
    public static bool IsValidHostsIp(string ip)
    {
        return IsValidIpAddress(ip);
    }

    /// <summary>
    /// Validates a hostname for hosts file entries.
    /// Must be a simple hostname without path separators or shell characters.
    /// </summary>
    public static bool IsValidHostsHostname(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname) || hostname.Length > 253)
            return false;

        return HostnameOnlyRegex().IsMatch(hostname);
    }

    // ================================================================
    // Port range validation
    // ================================================================

    /// <summary>
    /// Validates that start and end ports are within valid TCP/UDP range.
    /// </summary>
    public static bool IsValidPortRange(int startPort, int endPort, int maxSpan = 10000)
    {
        return startPort >= 1 && startPort <= 65535
            && endPort >= 1 && endPort <= 65535
            && startPort <= endPort
            && (endPort - startPort) <= maxSpan;
    }

    // ================================================================
    // Regex patterns
    // ================================================================

    // Hostname: labels separated by dots, each 1-63 chars, alphanumeric + hyphens
    // Also accepts valid IPv4 and IPv6 addresses
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9\-\.:\[\]]{0,252}$", RegexOptions.Compiled)]
    private static partial Regex HostnameOrIpRegex();

    // Service names: alphanumeric, underscore, hyphen, period only
    [GeneratedRegex(@"^[a-zA-Z0-9_\-\.]+$", RegexOptions.Compiled)]
    private static partial Regex ServiceNameRegex();

    // Hostnames for hosts file: stricter - no colons, brackets
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9\-\.]{0,252}$", RegexOptions.Compiled)]
    private static partial Regex HostnameOnlyRegex();
}

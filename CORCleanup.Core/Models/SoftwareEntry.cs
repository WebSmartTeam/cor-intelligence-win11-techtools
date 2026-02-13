namespace CORCleanup.Core.Models;

public sealed class SoftwareEntry
{
    public required string DisplayName { get; init; }
    public string? Publisher { get; init; }
    public string? DisplayVersion { get; init; }
    public string? InstallDate { get; init; }
    public long EstimatedSizeKb { get; init; }
    public string? InstallLocation { get; init; }
    public string? RegistryKey { get; init; }
    public bool IsSystemComponent { get; init; }

    public string SizeFormatted => ByteFormatter.FormatFromKb(EstimatedSizeKb);

    public string InstallDateFormatted
    {
        get
        {
            if (string.IsNullOrEmpty(InstallDate) || InstallDate.Length != 8) return "";
            // Registry stores as YYYYMMDD
            return $"{InstallDate[6..8]}/{InstallDate[4..6]}/{InstallDate[..4]}";
        }
    }
}

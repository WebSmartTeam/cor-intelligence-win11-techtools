namespace CORCleanup.Core.Models;

public sealed class InstalledProgram
{
    public required string DisplayName { get; init; }
    public string? Publisher { get; init; }
    public string? DisplayVersion { get; init; }
    public DateTime? InstallDate { get; init; }
    public long? EstimatedSizeBytes { get; init; }
    public string? UninstallString { get; init; }
    public string? InstallLocation { get; init; }
    public string? RegistryKeyPath { get; init; }
    public bool IsWindowsApp { get; init; }

    public string SizeFormatted =>
        EstimatedSizeBytes.HasValue
            ? ByteFormatter.Format(EstimatedSizeBytes.Value)
            : "Unknown";

    public string InstallDateFormatted =>
        InstallDate?.ToString("dd/MM/yyyy") ?? "Unknown";
}

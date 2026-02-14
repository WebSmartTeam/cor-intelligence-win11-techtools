namespace CORCleanup.Core.Models;

/// <summary>
/// A signed device driver with version and age information.
/// Used to identify outdated drivers that may need updating.
/// </summary>
public sealed class DriverInfo
{
    public required string DeviceName { get; init; }
    public required string DriverVersion { get; init; }
    public required string Manufacturer { get; init; }
    public required string DeviceClass { get; init; }
    public required DateTime DriverDate { get; init; }

    public int AgeYears => (int)((DateTime.Now - DriverDate).TotalDays / 365.25);
    public string DriverDateFormatted => DriverDate.ToString("dd/MM/yyyy");
    public string AgeSummary => AgeYears == 1 ? "1 year old" : $"{AgeYears} years old";
}

using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface ISystemInfoService
{
    Task<SystemInfo> GetSystemInfoAsync();
    Task<RamSummary> GetRamSummaryAsync();
    Task<List<DiskHealthInfo>> GetDiskHealthAsync();
    Task<BatteryInfo> GetBatteryInfoAsync();
    Task<string?> GetProductKeyAsync();
    Task<List<DriverInfo>> GetOutdatedDriversAsync(int olderThanYears = 3);
}

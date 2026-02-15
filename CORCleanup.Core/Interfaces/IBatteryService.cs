using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IBatteryService
{
    Task<BatteryInfo> GetBatteryInfoAsync();
}

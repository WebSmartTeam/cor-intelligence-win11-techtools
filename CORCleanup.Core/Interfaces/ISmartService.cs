using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface ISmartService
{
    Task<List<DiskHealthInfo>> GetAllDiskHealthAsync();
}

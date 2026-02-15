using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IDriverService
{
    Task<List<DriverInfo>> GetAllDriversAsync();
}

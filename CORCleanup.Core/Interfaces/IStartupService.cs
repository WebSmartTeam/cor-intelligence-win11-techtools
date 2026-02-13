using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IStartupService
{
    Task<List<StartupEntry>> GetStartupItemsAsync();
    Task<bool> SetEnabledAsync(StartupEntry entry, bool enabled);
}

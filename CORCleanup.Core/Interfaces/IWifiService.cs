using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IWifiService
{
    Task<List<WifiProfile>> GetSavedProfilesAsync();
}

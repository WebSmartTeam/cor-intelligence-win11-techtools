using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface INetworkInfoService
{
    Task<List<NetworkAdapterInfo>> GetAdaptersAsync();
    Task<string?> GetPublicIpAsync(CancellationToken ct = default);
}

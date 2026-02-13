using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IHostsFileService
{
    Task<List<HostsEntry>> ReadHostsFileAsync();
    Task SaveHostsFileAsync(IEnumerable<HostsEntry> entries);
    Task AddEntryAsync(string ipAddress, string hostname, string? comment = null);
    Task RemoveEntryAsync(string hostname);
    Task ToggleEntryAsync(string hostname);
}

using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IConnectionMonitorService
{
    Task<List<ConnectionEntry>> GetActiveConnectionsAsync();
    Task KillProcessAsync(int processId);
}

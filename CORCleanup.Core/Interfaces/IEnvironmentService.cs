using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IEnvironmentService
{
    Task<List<EnvironmentVariable>> GetAllVariablesAsync();
    Task SetVariableAsync(string name, string value, string scope);
    Task DeleteVariableAsync(string name, string scope);
    List<string> GetPathEntries(string scope);
    Task SetPathEntriesAsync(List<string> entries, string scope);
}

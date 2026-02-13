using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface ISoftwareInventoryService
{
    Task<List<SoftwareEntry>> GetInstalledSoftwareAsync(bool includeSystemComponents = false);
    Task ExportToCsvAsync(IEnumerable<SoftwareEntry> entries, string filePath);
}

using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IBsodViewerService
{
    Task<List<BsodCrashEntry>> GetCrashEntriesAsync();
}

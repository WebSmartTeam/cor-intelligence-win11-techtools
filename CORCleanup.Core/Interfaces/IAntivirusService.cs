using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IAntivirusService
{
    Task<List<AntivirusProduct>> ScanAsync(IProgress<string>? progress = null);
}

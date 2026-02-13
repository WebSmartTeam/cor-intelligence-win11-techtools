using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IUninstallService
{
    Task<List<InstalledProgram>> GetInstalledProgramsAsync();
    Task<bool> UninstallAsync(InstalledProgram program);
}

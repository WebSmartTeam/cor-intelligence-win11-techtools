using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IServicesManagerService
{
    Task<List<ServiceEntry>> GetServicesAsync();
    Task StartServiceAsync(string serviceName);
    Task StopServiceAsync(string serviceName);
    Task RestartServiceAsync(string serviceName);
    Task SetStartupTypeAsync(string serviceName, System.ServiceProcess.ServiceStartMode startMode);
}

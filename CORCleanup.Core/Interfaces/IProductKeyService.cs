namespace CORCleanup.Core.Interfaces;

public interface IProductKeyService
{
    Task<string?> GetOemKeyAsync();
    Task<string?> GetRegistryKeyAsync();
    Task<string> GetActivationStatusAsync();
}

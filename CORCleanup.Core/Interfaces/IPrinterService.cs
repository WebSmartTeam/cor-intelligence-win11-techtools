using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IPrinterService
{
    Task<List<PrinterInfo>> GetPrintersAsync();
    Task<bool> ClearSpoolerAsync();
    Task<bool> RemovePrinterAsync(string printerName);
    Task<bool> SetDefaultPrinterAsync(string printerName);
    Task<bool> PrintTestPageAsync(string printerName);
}

namespace CORCleanup.Core.Interfaces;

public interface IReportService
{
    Task<string> GenerateHtmlReportAsync(CancellationToken ct = default);
    Task SaveReportAsync(string htmlContent, string filePath, CancellationToken ct = default);
}

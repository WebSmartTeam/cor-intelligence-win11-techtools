using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IAutoToolService
{
    Task<DiagnosticReport> RunDiagnosticsAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    Task<AiRecommendation?> SubmitToAiAsync(
        DiagnosticReport report,
        CancellationToken ct = default);

    List<AutoToolAction> GetActionCatalogue();

    Task<string> ExecuteActionAsync(
        AutoToolAction action,
        DiagnosticReport report,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}

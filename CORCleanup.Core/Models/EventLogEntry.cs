namespace CORCleanup.Core.Models;

public enum EventSeverity
{
    Critical,
    Error,
    Warning,
    Information
}

public sealed class EventLogEntry
{
    public required DateTime TimeGenerated { get; init; }
    public required EventSeverity Severity { get; init; }
    public required string Source { get; init; }
    public required long EventId { get; init; }
    public required string LogName { get; init; }
    public required string Message { get; init; }
    public string? HumanReadableExplanation { get; init; }

    public string TimeFormatted => TimeGenerated.ToString("dd/MM/yyyy HH:mm:ss");
}

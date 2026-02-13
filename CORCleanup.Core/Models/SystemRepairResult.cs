namespace CORCleanup.Core.Models;

public sealed class SystemRepairResult
{
    public required string OperationName { get; init; }
    public required string Command { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Output { get; init; } = "";
    public string ErrorOutput { get; init; } = "";
    public TimeSpan Duration { get; init; }

    public string DurationFormatted => Duration.TotalSeconds < 60
        ? $"{Duration.TotalSeconds:F1}s"
        : $"{Duration.Minutes}m {Duration.Seconds}s";

    public string StatusDisplay => Success ? "Completed" : $"Failed (exit code {ExitCode})";
}

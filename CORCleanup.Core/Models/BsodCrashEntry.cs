namespace CORCleanup.Core.Models;

public sealed class BsodCrashEntry
{
    public required DateTime CrashTime { get; init; }
    public required string BugCheckCode { get; init; }
    public required string BugCheckName { get; init; }
    public string? FaultingModule { get; init; }
    public string? FaultingModulePath { get; init; }
    public required string DumpFilePath { get; init; }
    public required long DumpFileSizeBytes { get; init; }

    public string DumpFileName => System.IO.Path.GetFileName(DumpFilePath);
    public string DumpSizeFormatted => ByteFormatter.Format(DumpFileSizeBytes);
    public string CrashTimeFormatted => CrashTime.ToString("dd/MM/yyyy HH:mm:ss");
    public string FaultingDisplay => FaultingModule ?? "Unknown";
}

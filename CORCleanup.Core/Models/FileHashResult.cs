namespace CORCleanup.Core.Models;

public sealed class FileHashResult
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required long FileSizeBytes { get; init; }
    public required string Md5 { get; init; }
    public required string Sha1 { get; init; }
    public required string Sha256 { get; init; }

    public string FileSizeFormatted =>
        ByteFormatter.Format(FileSizeBytes);
}

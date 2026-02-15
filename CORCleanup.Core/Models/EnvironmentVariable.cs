namespace CORCleanup.Core.Models;

public sealed class EnvironmentVariable
{
    public required string Name { get; init; }
    public required string Value { get; set; }
    public required string Scope { get; init; }
    public bool IsPath => Name.Equals("PATH", StringComparison.OrdinalIgnoreCase)
                       || Name.Equals("Path", StringComparison.OrdinalIgnoreCase);
}

namespace CORCleanup.Core.Interfaces;

public interface IPasswordGeneratorService
{
    string Generate(PasswordOptions options);
}

public sealed class PasswordOptions
{
    public int Length { get; init; } = 16;
    public bool IncludeUppercase { get; init; } = true;
    public bool IncludeLowercase { get; init; } = true;
    public bool IncludeNumbers { get; init; } = true;
    public bool IncludeSymbols { get; init; } = true;
    public bool ExcludeAmbiguous { get; init; } = false;
}

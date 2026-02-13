using System.Security.Cryptography;
using CORCleanup.Core.Interfaces;

namespace CORCleanup.Core.Services.Tools;

public sealed class PasswordGeneratorService : IPasswordGeneratorService
{
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Numbers = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{}|;:',.<>?/~`";

    private const string AmbiguousChars = "0O1lI|";

    public string Generate(PasswordOptions options)
    {
        var charPool = BuildCharPool(options);
        if (charPool.Length == 0)
            throw new ArgumentException("At least one character set must be selected.");

        var length = Math.Max(4, Math.Min(128, options.Length));
        var result = new char[length];

        // Fill with cryptographically random characters
        for (var i = 0; i < length; i++)
        {
            result[i] = charPool[RandomNumberGenerator.GetInt32(charPool.Length)];
        }

        // Guarantee at least one character from each selected set
        var requiredSets = GetRequiredSets(options);
        for (var i = 0; i < requiredSets.Count && i < length; i++)
        {
            var set = requiredSets[i];
            if (options.ExcludeAmbiguous)
                set = RemoveAmbiguous(set);
            if (set.Length > 0)
                result[i] = set[RandomNumberGenerator.GetInt32(set.Length)];
        }

        // Shuffle to avoid predictable positions for guaranteed characters
        Shuffle(result);

        return new string(result);
    }

    private static string BuildCharPool(PasswordOptions options)
    {
        var pool = "";
        if (options.IncludeUppercase) pool += Uppercase;
        if (options.IncludeLowercase) pool += Lowercase;
        if (options.IncludeNumbers) pool += Numbers;
        if (options.IncludeSymbols) pool += Symbols;

        if (options.ExcludeAmbiguous)
            pool = RemoveAmbiguous(pool);

        return pool;
    }

    private static List<string> GetRequiredSets(PasswordOptions options)
    {
        var sets = new List<string>();
        if (options.IncludeUppercase) sets.Add(Uppercase);
        if (options.IncludeLowercase) sets.Add(Lowercase);
        if (options.IncludeNumbers) sets.Add(Numbers);
        if (options.IncludeSymbols) sets.Add(Symbols);
        return sets;
    }

    private static string RemoveAmbiguous(string input) =>
        new(input.Where(c => !AmbiguousChars.Contains(c)).ToArray());

    private static void Shuffle(char[] array)
    {
        for (var i = array.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }
}

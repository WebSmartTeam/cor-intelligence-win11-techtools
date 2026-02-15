using System.Collections;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.Core.Services.Admin;

/// <summary>
/// Reads and modifies Windows environment variables for both User and Machine scopes.
/// Machine-scope writes require administrator elevation.
/// Provides dedicated PATH management with broken-path detection.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EnvironmentService : IEnvironmentService
{
    public Task<List<EnvironmentVariable>> GetAllVariablesAsync()
    {
        return Task.Run(() =>
        {
            var variables = new List<EnvironmentVariable>();

            CollectVariables(variables, EnvironmentVariableTarget.User, "User");
            CollectVariables(variables, EnvironmentVariableTarget.Machine, "Machine");

            return variables.OrderBy(v => v.Scope).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    public Task SetVariableAsync(string name, string value, string scope)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Variable name must not be empty.", nameof(name));

            var target = ParseScope(scope);
            Environment.SetEnvironmentVariable(name, value, target);
        });
    }

    public Task DeleteVariableAsync(string name, string scope)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Variable name must not be empty.", nameof(name));

            var target = ParseScope(scope);

            // Setting to null removes the variable
            Environment.SetEnvironmentVariable(name, null, target);
        });
    }

    public List<string> GetPathEntries(string scope)
    {
        var target = ParseScope(scope);
        var pathValue = Environment.GetEnvironmentVariable("PATH", target);

        if (string.IsNullOrWhiteSpace(pathValue))
            return new List<string>();

        return pathValue
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
    }

    public Task SetPathEntriesAsync(List<string> entries, string scope)
    {
        return Task.Run(() =>
        {
            var target = ParseScope(scope);
            var joined = string.Join(';', entries.Where(e => !string.IsNullOrWhiteSpace(e)));
            Environment.SetEnvironmentVariable("PATH", joined, target);
        });
    }

    private static void CollectVariables(List<EnvironmentVariable> results, EnvironmentVariableTarget target, string scopeLabel)
    {
        var vars = Environment.GetEnvironmentVariables(target);

        foreach (DictionaryEntry entry in vars)
        {
            var name = entry.Key?.ToString();
            var value = entry.Value?.ToString();

            if (name is null) continue;

            results.Add(new EnvironmentVariable
            {
                Name = name,
                Value = value ?? "",
                Scope = scopeLabel
            });
        }
    }

    private static EnvironmentVariableTarget ParseScope(string scope)
    {
        return scope.ToUpperInvariant() switch
        {
            "USER" => EnvironmentVariableTarget.User,
            "MACHINE" => EnvironmentVariableTarget.Machine,
            _ => throw new ArgumentException(
                $"Invalid scope '{scope}'. Must be 'User' or 'Machine'.", nameof(scope))
        };
    }
}

using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IFirewallService
{
    Task<List<FirewallRule>> GetAllRulesAsync();
    Task SetRuleEnabledAsync(string ruleName, bool enabled);
}

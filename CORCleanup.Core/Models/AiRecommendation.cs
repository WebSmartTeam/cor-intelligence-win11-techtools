using System.Text.Json.Serialization;

namespace CORCleanup.Core.Models;

/// <summary>
/// AI response from the N8N webhook containing recommended action IDs and reasoning.
/// </summary>
public sealed class AiRecommendation
{
    [JsonPropertyName("recommendedActionIds")]
    public List<string> RecommendedActionIds { get; set; } = new();

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("actionReasons")]
    public Dictionary<string, string> ActionReasons { get; set; } = new();
}

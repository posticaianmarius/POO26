using System.Text.Json.Serialization;

public record ChatMessage
(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content
)
{
    [JsonIgnore] public ChatMetrics? Metrics {get; init;}
}
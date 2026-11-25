using System.Text.Json.Serialization;

// Chat message structure
public record ChatMessage([property: JsonPropertyName("role")] string Role, [property: JsonPropertyName("content")] string Content)
{
    // Internal performance metrics
    [JsonIgnore] public ChatMetrics? Metrics {get; init;}

    // Response statistics
    public record ChatMetrics(int? PromptTokens, int? CompletionTokens, TimeSpan Elapsed)
    {
        // Average response speed
        public double? TokensPerSecond => Elapsed > TimeSpan.Zero ? CompletionTokens / Elapsed.TotalSeconds : null;
    }
}

public record ChatMetrics
(
    int? PromptTokens,
    int? CompletionTokens,
    TimeSpan Elapsed
)
{
    public double? TokensPerSecond => Elapsed > TimeSpan.Zero ? CompletionTokens / Elapsed.TotalSeconds : null;
}
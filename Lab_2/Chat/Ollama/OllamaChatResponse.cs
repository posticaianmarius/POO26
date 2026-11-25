using System.Text.Json.Serialization;

namespace Zeya.Chat.Ollama;

internal sealed record OllamaChatResponse(
    [property: JsonPropertyName("message")] ChatMessage? Message,
    [property: JsonPropertyName("done_reason")] string? DoneReason,
    [property: JsonPropertyName("prompt_eval_count")] int? PromptTokens,
    [property: JsonPropertyName("eval_count")] int? CompletionTokens
);

using System.Text.Json;

public interface IChatModel
{
    // Maximum available prompt tokens
    int PromptTokenLimit {get;}

    // Estimate message token count
    int EstimateMessageTokens(string text);

    // Request an AI response with an optional JSON schema
    Task<ChatMessage> ChatAsync(IReadOnlyList<ChatMessage> messages, JsonElement? responseSchema = null);
}
public interface IChatModel
{
    // Maximum prompt token budget
    int PromptTokenLimit {get;}

    // Estimate message token count
    int EstimateMessageTokens(string text);

    // Generate assistant response
    Task<ChatMessage> ChatAsync(IReadOnlyList<ChatMessage> messages);
}
public interface IChatModel
{
    Task<ChatMessage> ChatAsync(IReadOnlyList<ChatMessage> messages);
}

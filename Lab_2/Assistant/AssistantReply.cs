public record AssistantReply
{
    public string Text {get;}
    public string UserMessage {get;}
    public IReadOnlyList<ChatMessage> MemoryContext {get;}

    public AssistantReply(string text, string userMessage, IReadOnlyList<ChatMessage> memoryContext)
    {
        Text = text;
        UserMessage = userMessage;

        // Preserve a read-only snapshot of the conversation context
        MemoryContext = Array.AsReadOnly(memoryContext.ToArray());
    }
}
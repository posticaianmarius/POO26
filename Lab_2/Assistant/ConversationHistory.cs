public class ConversationHistory
{
    private readonly List<Exchange> exchanges = [];
    private readonly Logger logger = new("ConversationHistory");

    // Retrieve recent exchanges within the token budget
    public ChatMessage[] GetChatContext(long tokenBudget)
    {
        int start = exchanges.Count;
        while(start > 0 && exchanges[start - 1].TokenCount <= tokenBudget)
        {
            start --;
            tokenBudget -= exchanges[start].TokenCount;
        }

        if(start > 0) logger.Log(Logger.LogLevel.Warn, $"Omitted {start} exchanges from context.");

        var context = new ChatMessage[checked((exchanges.Count - start) * 2)];
        for(int index = start, offset = 0; index < exchanges.Count; index ++, offset += 2)
        {
            context[offset] = exchanges[index].User;
            context[offset + 1] = exchanges[index].Reply;
        }
        return context;
    }

    // Store an exchange with its token count
    public void AddExchange(ChatMessage user, ChatMessage reply, int tokenCount)
    {
        exchanges.Add(new Exchange(user, reply, tokenCount));
        logger.Log(Logger.LogLevel.Trace, $"Exchange added. Tokens: {tokenCount}.");
    }

    // Clear conversation history
    public void Clear() => exchanges.Clear();

    // User-assistant message pair
    private record Exchange(ChatMessage User, ChatMessage Reply, int TokenCount);
}
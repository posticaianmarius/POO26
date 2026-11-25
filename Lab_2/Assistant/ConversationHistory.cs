
public class ConversationHistory
{
    private readonly List<Exchange> exchanges = [];
    private readonly Logger logger = new("ConversationHistory");

    // Select history within budget
    public ChatMessage[] GetChatContext(int historyBudget)
    {
        int omittedExchanges = FindFirstExchangeWithinBudget(historyBudget);
        if(omittedExchanges > 0) logger.Log(Logger.LogLevel.Warn, $"Omitted {omittedExchanges} exchanges.");

        var context = new ChatMessage[(exchanges.Count - omittedExchanges) * 2];

        for(int index = omittedExchanges; index < exchanges.Count; index ++)
        {
            int offset = (index - omittedExchanges) * 2;
            context[offset] = exchanges[index].User;
            context[offset + 1] = exchanges[index].Reply;
        }

        return context;
    }

    // Store exchange
    internal void AddExchange(ChatMessage user, ChatMessage reply, int tokens)
    {
        exchanges.Add(new Exchange(user, reply, tokens));
        logger.Log(Logger.LogLevel.Trace, "Exchange added.");
    }

    public void Clear() => exchanges.Clear();

    // Find history start index
    private int FindFirstExchangeWithinBudget(int budget)
    {
        int index = exchanges.Count;
        while(index > 0 && exchanges[index - 1].Tokens <= budget)
        {
            index --;
            budget -= exchanges[index].Tokens;
        }
        return index;
    }

    private sealed record Exchange(ChatMessage User, ChatMessage Reply, int Tokens);
}

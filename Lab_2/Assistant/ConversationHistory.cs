
namespace Zeya.Assistant;

public sealed class ConversationHistory
{
    private readonly List<ChatMessage> messages = [];
    private readonly int exchangeCapacity;
    private readonly Logger logger = new("ConversationHistory");

    private ConversationHistory(int exchangeCapacity) => this.exchangeCapacity = exchangeCapacity;
    public static ConversationHistory Tier1() => new(2);
    public static ConversationHistory Tier2() => new(6);
    public static ConversationHistory Tier3() => new(10);

    public ChatMessage[] GetChatContext() => messages.ToArray();

    internal void AddExchange(ChatMessage user, ChatMessage reply)
    {
        if(messages.Count == exchangeCapacity * 2)
        {
            messages.RemoveRange(0, 2);
            logger.Log(Logger.LogLevel.Warn, "History full. Dropped the oldest exchange.");
        }
        messages.Add(user);
        messages.Add(reply);
        logger.Log(Logger.LogLevel.Trace, "Exchange added.");
    }

    public void Clear() => messages.Clear();
}

using System.Text.Json;

public class AssistantSession
{
    private const int MaximumToolCalls = 3;
    private readonly Toolbox toolbox = new();
    private readonly Logger logger = new("AssistantSession");

    private readonly IChatModel chatModel;
    private readonly ConversationHistory conversation;
    private readonly int historyBudgetPercent;

    public AssistantSession(IChatModel chatModel, ConversationHistory conversation, int historyBudgetPercent)
    {
        this.chatModel = chatModel;
        this.conversation = conversation;
        this.historyBudgetPercent = historyBudgetPercent;
    }

    // Generate reply and store exchange
    public async Task<string> ReplyAsync(string input)
    {
        ChatMessage system = new ChatMessage("system", AssistantSettings.SystemPrompt);
        ChatMessage user = new ChatMessage("user", input);

        int userTokens = chatModel.EstimateMessageTokens(user.Content);
        ChatMessage answer = await ReplyWithToolsAsync(system, user, userTokens);

        int exchangeTokens = userTokens + chatModel.EstimateMessageTokens(answer.Content);
        conversation.AddExchange(user, answer, exchangeTokens);

        return answer.Content;
    }

    // Generate reply with tools
    private async Task<ChatMessage> ReplyWithToolsAsync(ChatMessage system, ChatMessage user, int userTokens)
    {
        List<ChatMessage> turn = [user];

        // Calculate available context
        long availableTokens = (long)chatModel.PromptTokenLimit - chatModel.EstimateMessageTokens(system.Content) - userTokens;

        for(int toolCalls = 0; ; toolCalls ++)
        {
            long historyBudget = availableTokens * historyBudgetPercent / 100;
            ChatMessage[] history = conversation.GetChatContext(historyBudget);

            logger.Log(Logger.LogLevel.Trace, $"History: {history.Length} messages, budget: {historyBudget} tokens.");

            ChatMessage answer = await chatModel.ChatAsync([system, .. history, .. turn]);

            if(!TryReadToolCall(answer.Content, out string toolName, out string input)) return answer;

            // Enforce tool limit
            if(toolCalls == MaximumToolCalls)
            {
                const string message = "Tool call limit reached.";
                logger.Log(Logger.LogLevel.Error, message);
                return new ChatMessage("assistant", message);
            }

            // Execute tool
            logger.Log(Logger.LogLevel.Trace, $"Tool requested: {toolName}.");

            string result = toolbox.Run(toolName, input);
            var toolResult = new ChatMessage("user", $"Tool result: {result}");

            turn.Add(answer);
            turn.Add(toolResult);

            // Update token budget
            availableTokens -= (long)chatModel.EstimateMessageTokens(answer.Content) + chatModel.EstimateMessageTokens(toolResult.Content);
        }
    }

    // Parse tool request
    private static bool TryReadToolCall(string answer, out string toolName, out string input)
    {
        toolName = "";
        input = "";

        int start = answer.IndexOf('{');
        int end = answer.LastIndexOf('}');
        if(start < 0 || end < start || !answer.AsSpan(start).Contains("\"tool\"", StringComparison.Ordinal)) return false;

        using var document = JsonDocument.Parse(answer[start..(end + 1)]);
        JsonElement call = document.RootElement;

        if(!call.TryGetProperty("tool", out JsonElement tool) || tool.ValueKind != JsonValueKind.String) return false;
        if(!call.TryGetProperty("input", out JsonElement toolInput) || toolInput.ValueKind != JsonValueKind.String) return false;

        toolName = tool.GetString()!.Trim();
        input = toolInput.GetString()!;

        return toolName.Length > 0;
    }

    // Clear conversation
    public void ClearConversation()
    {
        conversation.Clear();
        logger.Log(Logger.LogLevel.Info, "Conversation cleared.");
    }
}
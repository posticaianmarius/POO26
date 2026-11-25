
using System.Text.Json;
using Zeya.Chat;
using Zeya.Memory;

namespace Zeya.Assistant;

public sealed class AssistantSession
{
    private const int MaximumToolCalls = 3;
    private readonly int historyBudgetPercent;
    private readonly IChatModel chatModel;
    private readonly MemoryService memoryService;
    private readonly ConversationHistory conversation;
    private readonly Toolbox toolbox = new();
    private readonly Logger logger = new("Zeya");

    public IReadOnlyList<MemoryFact> SavedFacts => memoryService.SavedFacts;

    public AssistantSession(IChatModel chatModel, MemoryService memoryService) : this(chatModel, memoryService, new ConversationHistory()) {}

    public AssistantSession(IChatModel chatModel, MemoryService memoryService, ConversationHistory conversation, int historyBudgetPercent = 75)
    {
        this.chatModel = chatModel;
        this.memoryService = memoryService;
        this.conversation = conversation;
        this.historyBudgetPercent = historyBudgetPercent;
    }

    // Generate reply and store exchange
    public async Task<AssistantReply> ReplyAsync(string input)
    {
        ChatMessage[] memoryContext = conversation.GetChatContext(MemoryService.RecentContextTokenBudget);
        
        string instructions = $"{AssistantInstructions.SystemPrompt}\n{memoryService.GetSavedFactsContext()}";
        var system = new ChatMessage("system", instructions);
        var user = new ChatMessage("user", input);
        ChatMessage answer = await ReplyWithToolsAsync(system, user);

        int tokens = checked(chatModel.EstimateMessageTokens(user.Content) + chatModel.EstimateMessageTokens(answer.Content));
        conversation.AddExchange(user, answer, tokens);

        return new AssistantReply(answer.Content, input, memoryContext);
    }

    // Generate reply with tools
    private async Task<ChatMessage> ReplyWithToolsAsync(ChatMessage system, ChatMessage user)
    {
        List<ChatMessage> turn = [user];
        int availableTokens = chatModel.PromptTokenLimit - chatModel.EstimateMessageTokens(system.Content) - chatModel.EstimateMessageTokens(user.Content);

        for(int toolCalls = 0; ; toolCalls ++)
        {
            if(availableTokens < 0) throw new InvalidDataException("The current chat turn exceeds the token budget.");

            int historyBudget = GetHistoryBudget(availableTokens);
            ChatMessage[] history = conversation.GetChatContext(historyBudget);
            ChatMessage answer = await chatModel.ChatAsync([system, .. history, .. turn]);

            if(answer is not { Role: "assistant" } || string.IsNullOrWhiteSpace(answer.Content))
            {
                throw new InvalidOperationException("IChatModel must return a nonempty assistant message.");
            }

            if(!TryReadToolCall(answer.Content, out string toolName, out string input)) return answer;

            if(toolCalls == MaximumToolCalls)
            {
                const string message = "Tool call limit reached. Try a simpler question.";
                logger.Log(Logger.LogLevel.Error, message);
                throw new InvalidDataException(message);
            }

            string result = toolName.Length == 0 ? "Error: send a JSON object with a tool name and a string input." : toolbox.Run(toolName, input);
            if(result.StartsWith("Error:", StringComparison.Ordinal)) logger.Log(Logger.LogLevel.Error, $"Tool request failed. {result}");

            // Append tool result
            var toolResult = new ChatMessage("user", $"Tool result: {result}");
            turn.Add(answer);
            turn.Add(toolResult);
            availableTokens -= chatModel.EstimateMessageTokens(answer.Content) + chatModel.EstimateMessageTokens(toolResult.Content);
        }
    }

    // Parse tool request
    private static bool TryReadToolCall(string answer, out string toolName, out string input)
    {
        toolName = "";
        input = "";
        int start = answer.IndexOf('{');
        if(start < 0 || !answer.AsSpan(start).Contains("\"tool\"", StringComparison.Ordinal)) return false;
        int end = answer.LastIndexOf('}');
        if(end < start) return true;

        try
        {
            using var document = JsonDocument.Parse(answer[start..(end + 1)]);
            JsonElement call = document.RootElement;
            if(!call.TryGetProperty("tool", out JsonElement tool)) return false;

            if(tool.ValueKind != JsonValueKind.String || !call.TryGetProperty("input", out JsonElement toolInput) || toolInput.ValueKind != JsonValueKind.String)
            {
                return true;
            }

            toolName = tool.GetString()!.Trim();
            input = toolInput.GetString()!;
            return true;
        }
        catch(JsonException)
        {
            return true;
        }
    }

    // Save permanent memories
    public async Task<MemoryChangeSummary> SaveMemoriesAsync(AssistantReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        MemoryChangeSummary changes = await memoryService.ExtractAndSaveAsync(reply.UserMessage, reply.MemoryContext);

        if(changes.Deleted > 0) conversation.Clear();
        return changes;
    }

    // Forget memory
    public async Task<bool> ForgetMemoryAsync(int memoryId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(memoryId);
        bool removed = await memoryService.ForgetAsync(memoryId);

        if(removed) conversation.Clear();
        return removed;
    }

    public void ClearConversation() => conversation.Clear();

    // Calculate history budget
    private int GetHistoryBudget(int availableTokens) => (int)((long)availableTokens * historyBudgetPercent / 100);
}

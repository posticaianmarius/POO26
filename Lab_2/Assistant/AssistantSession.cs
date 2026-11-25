
using System.Text.Json;

namespace Zeya.Assistant;

public sealed class AssistantSession
{
    private const int MaximumToolCalls = 3;
    private readonly IChatModel chatModel;
    private readonly ConversationHistory conversation;
    private readonly Toolbox toolbox = new();
    private readonly Logger logger = new("Zeya");

    public AssistantSession(IChatModel chatModel, ConversationHistory conversation)
    {
        ArgumentNullException.ThrowIfNull(chatModel);
        ArgumentNullException.ThrowIfNull(conversation);
        this.chatModel = chatModel;
        this.conversation = conversation;
    }

    // Generate reply and store exchange
    public async Task<string> ReplyAsync(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        var system = new ChatMessage("system", AssistantInstructions.SystemPrompt);
        var user = new ChatMessage("user", input);
        List<ChatMessage> request = [system, .. conversation.GetChatContext(), user];
        ChatMessage answer = await ReplyWithToolsAsync(request);
        conversation.AddExchange(user, answer);
        return answer.Content;
    }

    // Generate reply with tools
    private async Task<ChatMessage> ReplyWithToolsAsync(List<ChatMessage> request)
    {
        for(int toolCalls = 0; ; toolCalls ++)
        {
            ChatMessage answer = await chatModel.ChatAsync(request);

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

            request.Add(answer);
            request.Add(new ChatMessage("user", $"Tool result: {result}"));
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

    public void ClearConversation() => conversation.Clear();
}

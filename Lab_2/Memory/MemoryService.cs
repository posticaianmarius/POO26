
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zeya.Memory;

public class MemoryService
{
    internal const int RecentContextTokenBudget = 512;

    // Detect temporary instructions
    private const string SessionOnlyPattern =
        @"\b(?:" +
        @"(?:for|during|in)\s+(?:this|the current)\s+(?:chat|conversation|session)" +
        @"|(?:do not|don't|never)\s+(?:save|store)" +
        @")\b";

    // Expected AI response structure
    private static readonly JsonElement ResponseSchema = JsonSerializer.Deserialize<JsonElement>(MemoryPrompts.ResponseSchemaJson);

    private readonly IChatModel chatModel;
    private readonly IMemoryStore memoryStore;
    private int operationInProgress;

    public MemoryService(IChatModel chatModel, IMemoryStore memoryStore)
    {
        this.chatModel = chatModel;
        this.memoryStore = memoryStore;
    }

    // Retrieve valid memories
    public IReadOnlyList<MemoryFact> SavedFacts => memoryStore.Facts;
    public string GetSavedFactsContext() => "SAVED MEMORIES: " + SerializeSavedFacts();

    // Remove saved memory
    public async Task<bool> ForgetAsync(int memoryId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(memoryId);
        BeginOperation();
        try
        {
            return await memoryStore.ForgetAsync(memoryId);
        }
        finally
        {
            EndOperation();
        }
    }

    // Extract and save memories
    public async Task<MemoryChangeSummary> ExtractAndSaveAsync(string userMessage, IReadOnlyList<ChatMessage> recentMessages)
    {
        BeginOperation();
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
            ArgumentNullException.ThrowIfNull(recentMessages);

            if(recentMessages.Any(message => message is null || string.IsNullOrWhiteSpace(message.Role) || string.IsNullOrWhiteSpace(message.Content)))
            {
                throw new ArgumentException("Recent messages must contain a role and content.", nameof(recentMessages));
            }

            if(IsSessionOnlyRequest(userMessage)) return new MemoryChangeSummary(0, 0, 0);

            string instructions = string.Join("\n", MemoryPrompts.ExtractionInstructions, MemoryPrompts.ScopeExample, "JSON SCHEMA: " + ResponseSchema.GetRawText());
            ChatMessage[] request = CreateExtractionRequest(instructions, userMessage, recentMessages);
            string proposalJson = await RequestProposalAsync(request);

            try
            {
                return await ApplyProposalAsync(proposalJson, userMessage);
            }
            catch(Exception error) when(error is JsonException or InvalidDataException)
            {
                // Retry invalid proposals
                string repairInstructions = string.Join("\n", instructions, "The previous proposal was rejected: " + error.Message, MemoryPrompts.RepairInstructions);
                ChatMessage[] repairRequest = CreateExtractionRequest(repairInstructions, userMessage, recentMessages);
                string repairedJson = await RequestProposalAsync(repairRequest);
                return await ApplyProposalAsync(repairedJson, userMessage);
            }
        }
        finally
        {
            EndOperation();
        }
    }

    // Request memory proposal
    private async Task<string> RequestProposalAsync(IReadOnlyList<ChatMessage> request)
    {
        ChatMessage answer = await chatModel.ChatAsync(request, ResponseSchema);
        if(answer is not { Role: "assistant" } || string.IsNullOrWhiteSpace(answer.Content))
        {
            throw new InvalidOperationException("IChatModel must return a nonempty assistant message.");
        }
        return answer.Content;
    }

    // Apply memory changes
    private async Task<MemoryChangeSummary> ApplyProposalAsync(string json, string userMessage)
    {
        var proposal = JsonSerializer.Deserialize<MemoryUpdateBatch>(json, MemoryJson.Options);

        if(proposal?.Actions is null) throw new InvalidDataException("Missing memory actions.");

        return await memoryStore.ApplyUpdatesAsync(proposal.Actions, userMessage) ?? throw new InvalidOperationException("IMemoryStore must return a change summary.");
    }

    // Fit extraction request within prompt budget
    private ChatMessage[] CreateExtractionRequest(string instructions, string userMessage, IReadOnlyList<ChatMessage> recentMessages)
    {
        int payloadBudget = chatModel.PromptTokenLimit - chatModel.EstimateMessageTokens(instructions);

        for(int omitted = 0; omitted <= recentMessages.Count; omitted ++)
        {
            string payload = CreateExtractionPayload(userMessage, recentMessages.Skip(omitted));
            if(chatModel.EstimateMessageTokens(payload) <= payloadBudget)
            {
                return [new ChatMessage("system", instructions), new ChatMessage("user", payload)];
            }
        }

        throw new InvalidDataException("The memory request exceeds the token budget.");
    }

    // Build extraction payload
    private string CreateExtractionPayload(string userMessage, IEnumerable<ChatMessage> recentMessages)
    {
        var payload = new
        {
            newUserMessage = userMessage,
            recentContext = recentMessages,
            existingMemories = SavedFacts.Select(memory => new
            {
                memory.Id,
                memory.Text,
                memory.Scope
            })
        };

        return JsonSerializer.Serialize(payload, MemoryJson.Options);
    }

    // Serialize saved memories
    private string SerializeSavedFacts()
    {
        var facts = SavedFacts.Select(memory => new
        {
            memory.Id,
            memory.Text,
            memory.Scope
        });

        return JsonSerializer.Serialize(facts, MemoryJson.Options);
    }

    // Prevent overlapping operations
    private void BeginOperation()
    {
        if(Interlocked.CompareExchange(ref operationInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException("Finish the current memory request before starting another.");
        }
    }

    private void EndOperation() => Volatile.Write(ref operationInProgress, 0);

    // Detect session-only requests
    private static bool IsSessionOnlyRequest(string userMessage)
    {
        return Regex.IsMatch(userMessage, SessionOnlyPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

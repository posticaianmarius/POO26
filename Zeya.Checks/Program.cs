using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

if(args is ["--live"])
{
    using var http = new HttpClient { BaseAddress = AssistantSettings.OllamaAddress, Timeout = AssistantSettings.RequestTimeout };
    var assistant = new AssistantSession(new OllamaChatModel(http, AssistantSettings.ModelName), new ConversationHistory(), 50);
    await assistant.ReplyAsync("My name is Ada. Reply in one word.");
    string name = await assistant.ReplyAsync("What is my name? Reply with only my name.");
    Assert(name.Contains("Ada", StringComparison.OrdinalIgnoreCase), "The model did not recall the name from this chat.");
    string result = await assistant.ReplyAsync("What is 12 times 7? Reply briefly.");
    Assert(result.Contains("84", StringComparison.Ordinal), "The model did not return the expected calculation.");
    Console.WriteLine("PASS live recall and calculation");
    return;
}

if(args.Length != 0)
{
    Console.Error.WriteLine("Usage: Zeya.Checks [--live]");
    Environment.ExitCode = 2;
    return;
}

await Check("history keeps every exchange and respects each tier's token share", async () =>
{
    const int availableTokens = 120;
    const int messageLength = 10;
    const int turnCount = 9;
    int exchangeTokens = messageLength + "reply 1".Length;
    int[] tierPercentages = [50, 75, 100];
    foreach(int percentage in tierPercentages)
    {
        var history = new ConversationHistory();
        var model = new RecordingChatModel();
        var assistant = new AssistantSession(model, history, percentage);
        int recentCount = availableTokens * percentage / 100 / exchangeTokens;
        for(int turn = 1; turn <= turnCount; turn ++)
        {
            await assistant.ReplyAsync($"turn {turn}".PadRight(messageLength, '.'));
            if(turn == 1) model.PromptTokenLimit = model.Requests[0][0].Content.Length + messageLength + availableTokens;
            Assert(model.Requests[^1].Length == Math.Min(turn - 1, recentCount) * 2 + 2, "The request used the wrong history size.");
            Assert(history.GetChatContext(long.MaxValue).Length == turn * 2, "An exchange was removed from history.");
        }

        ChatMessage[] request = model.Requests[^1];
        Assert(request[0].Role == "system", "The system message was misplaced.");
        for(int index = 0; index < recentCount; index ++)
        {
            int turn = turnCount - recentCount + index;
            Assert(request[index * 2 + 1].Role == "user" && request[index * 2 + 1].Content == $"turn {turn}".PadRight(messageLength, '.'), "An old user message was retained or reordered.");
            Assert(request[index * 2 + 2].Role == "assistant" && request[index * 2 + 2].Content == $"reply {turn}", "A reply was split from its user message.");
        }
        Assert(request[^1].Content == $"turn {turnCount}".PadRight(messageLength, '.'), "The current turn was misplaced.");
        Assert(history.GetChatContext(long.MaxValue)[0].Content == "turn 1".PadRight(messageLength, '.'), "The oldest exchange was lost.");
        Assert(history.GetChatContext(0).Length == 0, "A zero-token budget returned messages.");
        Assert(model.Requests.Count == turnCount, "An ordinary turn made an extra model request.");
    }
});

await Check("the token budget uses saved counts to omit older pairs", async () =>
{
    var model = new RecordingChatModel();
    var history = new ConversationHistory();
    var assistant = new AssistantSession(model, history, 100);
    await assistant.ReplyAsync("first");
    await assistant.ReplyAsync("second");

    model.PromptTokenLimit = model.Requests[0][0].Content.Length + "second".Length + "reply 2".Length + "third".Length;
    await assistant.ReplyAsync("third");
    ChatMessage[] request = model.Requests[^1];
    Assert(request.Length == 4 && request[1].Content == "second" && request[2].Content == "reply 2", "The budget did not omit a complete older pair.");
    ChatMessage[] stored = history.GetChatContext(long.MaxValue);
    Assert(stored.Length == 6 && stored[0].Content == "first", "Budgeting changed the stored history.");
    foreach(string text in new[] { "first", "reply 1", "second", "reply 2", "third", "reply 3" })
    {
        Assert(model.EstimatedTexts.Count(estimated => estimated == text) == 1, "A saved message was counted again.");
    }
});

await Check("tool replies stay within the current turn", async () =>
{
    const string call = """{"tool":"calculator","input":"12 * 7"}""";
    var model = new RecordingChatModel(call, "84");
    var assistant = new AssistantSession(model, new ConversationHistory(), 75);
    Assert(await assistant.ReplyAsync("What is 12 times 7?") == "84", "The assistant lost the final answer.");
    Assert(model.Requests.Count == 2 && model.Requests[1][^2].Content == call, "The assistant tool call was lost.");
    Assert(model.Requests[1][^1] is {Role: "user", Content: "Tool result: 84"}, "The tool reply did not reach the model.");
    Assert(model.EstimatedTexts.Contains(call) && model.EstimatedTexts.Contains("Tool result: 84"), "Tool calls and replies were not budgeted.");

    await assistant.ReplyAsync("Repeat it.");
    ChatMessage[] request = model.Requests[^1];
    Assert(request.Length == 4 && request[2].Content == "84", "Intermediate tool messages leaked into history.");
});

await Check("unknown tools are reported and repeated calls are bounded", async () =>
{
    const string call = """{"tool":"unknown","input":""}""";
    var model = new RecordingChatModel(call, call, call, call);
    var history = new ConversationHistory();
    var assistant = new AssistantSession(model, history, 50);
    string reply = await assistant.ReplyAsync("Use a tool.");
    Assert(reply.StartsWith("Tool call limit", StringComparison.Ordinal) && model.Requests.Count == 4, "The assistant did not stop after three tool calls.");
    Assert(model.Requests[^1].Count(message => message.Role == "user" && message.Content.StartsWith("Tool result: Error:", StringComparison.Ordinal)) == 3, "Tool errors did not reach the model.");
    Assert(history.GetChatContext(long.MaxValue).Length == 2, "Intermediate tool messages leaked into history.");
});

await Check("the toolbox provides time, date, and calculator", () =>
{
    var tools = new Toolbox();
    Assert(tools.Run("calculator", "12 * 7") == "84", "The calculator returned the wrong result.");
    Assert(tools.Run("calculator", "12 / 0").StartsWith("Error:", StringComparison.Ordinal), "Division by zero was accepted.");
    Assert(TimeOnly.TryParse(tools.Run("get_time", ""), out _), "The clock returned invalid text.");
    Assert(DateOnly.TryParseExact(tools.Run("get_date", ""), "yyyy-MM-dd", out _), "The date tool returned invalid text.");
    return Task.CompletedTask;
});

await Check("the console recovers from an unavailable model and clears history", async () =>
{
    var model = new RecordingChatModel { NextFailure = new HttpRequestException("Connection refused.") };
    var history = new ConversationHistory();
    var console = new ConsoleChat(new AssistantSession(model, history, 75));
    TextReader originalInput = Console.In;
    TextWriter originalOutput = Console.Out;
    using var input = new StringReader("First question\nSecond question\n/clear\nquit\n");
    using var output = new StringWriter();

    try
    {
        Console.SetIn(input);
        Console.SetOut(output);
        await console.RunAsync();
    }
    finally
    {
        Console.SetIn(originalInput);
        Console.SetOut(originalOutput);
    }

    string transcript = output.ToString();
    Assert(transcript.Contains("Ollama unavailable.") && transcript.Contains("AI: reply 2"), "The console did not continue after the request failed.");
    Assert(model.Requests[^1].Length == 2, "A failed turn was included in the next request.");
    Assert(transcript.Contains("Chat cleared.") && history.GetChatContext(long.MaxValue).Length == 0, "The clear command left messages in memory.");
});

await Check("Ollama counts tokens and keeps usage metrics out of outgoing messages", async () =>
{
    using var handler = new StubHandler();
    using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
    var model = new OllamaChatModel(http, "qwen3.5:4b-q4_K_M");
    Assert(model.EstimateMessageTokens("Hello world!") == 19, "Qwen should count three text tokens plus sixteen for framing.");
    Assert(new OllamaChatModel(http, "test").EstimateMessageTokens("é") == 18, "An unregistered model should use a UTF-8 estimate.");
    var user = new ChatMessage("user", "Hello");
    ChatMessage answer = await model.ChatAsync([user]);
    Assert(answer.Metrics is { PromptTokens: 123, CompletionTokens: 24, TokensPerSecond: > 0 }, "Ollama usage was not recorded.");

    await model.ChatAsync([user, answer, new ChatMessage("user", "Continue")]);
    using var request = JsonDocument.Parse(handler.RequestJson);
    JsonElement reply = request.RootElement.GetProperty("messages")[1];
    Assert(reply.EnumerateObject().Count() == 2 && reply.GetProperty("role").GetString() == "assistant", "Metrics leaked into an outgoing chat message.");
    Assert(!request.RootElement.TryGetProperty("tools", out _), "The request still includes native tool definitions.");
});

static async Task Check(string name, Func<Task> run)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {name}");
    }
    catch
    {
        Console.Error.WriteLine($"FAIL {name}");
        throw;
    }
}

static void Assert(bool condition, string message)
{
    if(!condition) throw new InvalidOperationException(message);
}

class RecordingChatModel(params string[] responses) : IChatModel
{
    public int PromptTokenLimit {get; set;} = 7168;
    public List<ChatMessage[]> Requests {get;} = [];
    public List<string> EstimatedTexts {get;} = [];
    public Exception? NextFailure {get; set;}

    public int EstimateMessageTokens(string text)
    {
        EstimatedTexts.Add(text);
        return text.Length;
    }

    public Task<ChatMessage> ChatAsync(IReadOnlyList<ChatMessage> messages)
    {
        Requests.Add(messages.ToArray());
        if(NextFailure is Exception failure)
        {
            NextFailure = null;
            return Task.FromException<ChatMessage>(failure);
        }

        string reply = Requests.Count <= responses.Length ? responses[Requests.Count - 1] : $"reply {Requests.Count}";
        return Task.FromResult(new ChatMessage("assistant", reply));
    }
}

class StubHandler : HttpMessageHandler
{
    public string RequestJson {get; private set;} = "";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpContent content = request.Content ?? throw new InvalidOperationException("Expected a request body.");
        RequestJson = await content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { message = new { role = "assistant", content = "Hello!" }, prompt_eval_count = 123, eval_count = 24 })
        };
    }
}

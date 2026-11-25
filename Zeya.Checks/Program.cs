using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Zeya.Assistant;
using Zeya.Chat.Ollama;
using Zeya.ConsoleUI;

if(args is ["--live"])
{
    using var http = new HttpClient { BaseAddress = AssistantSettings.OllamaAddress, Timeout = AssistantSettings.RequestTimeout };
    var assistant = new AssistantSession(new OllamaChatModel(http, AssistantSettings.ModelName), ConversationHistory.Tier1());
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

await Check("tiers retain the newest complete exchanges", async () =>
{
    (ConversationHistory History, int Capacity)[] tiers = [(ConversationHistory.Tier1(), 2), (ConversationHistory.Tier2(), 6), (ConversationHistory.Tier3(), 10)];
    foreach(var (history, capacity) in tiers)
    {
        var model = new RecordingChatModel();
        var assistant = new AssistantSession(model, history);
        for(int turn = 1; turn <= capacity + 2; turn ++)
        {
            await assistant.ReplyAsync($"turn {turn}");
            Assert(history.GetChatContext().Length == Math.Min(turn, capacity) * 2, "The history exceeded its capacity.");
        }

        ChatMessage[] request = model.Requests[^1];
        Assert(request.Length == capacity * 2 + 2 && request[0].Role == "system", "The request used the wrong history size.");
        for(int index = 0; index < capacity; index ++)
        {
            int turn = index + 2;
            Assert(request[index * 2 + 1].Role == "user" && request[index * 2 + 1].Content == $"turn {turn}", "An old user message was retained or reordered.");
            Assert(request[index * 2 + 2].Role == "assistant" && request[index * 2 + 2].Content == $"reply {turn}", "A reply was split from its user message.");
        }
        Assert(request[^1].Content == $"turn {capacity + 2}" && history.GetChatContext()[0].Content == "turn 3", "The current turn did not replace the oldest exchange.");
        Assert(model.Requests.Count == capacity + 2, "An ordinary turn made an extra model request.");
    }
});

await Check("tool replies stay within the current turn", async () =>
{
    var model = new RecordingChatModel("Sure! {\"tool\":\"calculator\",\"input\":\"12 * 7\"}", "84");
    var assistant = new AssistantSession(model, ConversationHistory.Tier2());
    Assert(await assistant.ReplyAsync("What is 12 times 7?") == "84", "The assistant lost the final answer.");
    Assert(model.Requests.Count == 2 && model.Requests[1][^1].Content == "Tool result: 84", "The tool result did not reach the model.");

    await assistant.ReplyAsync("Repeat it.");
    ChatMessage[] request = model.Requests[^1];
    Assert(request.Length == 4 && request[2].Content == "84", "Intermediate tool messages leaked into history.");
});

await Check("unknown tools are reported and repeated calls are bounded", async () =>
{
    const string toolCall = "{\"tool\":\"unknown\",\"input\":\"\"}";
    var model = new RecordingChatModel(toolCall, toolCall, toolCall, toolCall);
    var history = ConversationHistory.Tier1();
    var assistant = new AssistantSession(model, history);
    try
    {
        await assistant.ReplyAsync("Use a tool.");
        throw new InvalidOperationException("The assistant did not stop the tool loop.");
    }
    catch(InvalidDataException error) when(error.Message.StartsWith("Tool call limit", StringComparison.Ordinal))
    {
        Assert(model.Requests.Count == 4 && model.Requests[1][^1].Content.StartsWith("Tool result: Error:", StringComparison.Ordinal), "Tool errors or the retry limit were lost.");
        Assert(history.GetChatContext().Length == 0, "A failed turn changed history.");
    }
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
    var history = ConversationHistory.Tier2();
    var console = new ConsoleChat(new AssistantSession(model, history));
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
    Assert(transcript.Contains("Chat cleared.") && history.GetChatContext().Length == 0, "The clear command left messages in memory.");
});

await Check("Ollama attaches usage metrics and keeps them out of outgoing messages", async () =>
{
    using var handler = new StubHandler();
    using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
    var model = new OllamaChatModel(http, "test");
    var user = new ChatMessage("user", "Hello");
    ChatMessage answer = await model.ChatAsync([user]);
    Assert(answer.Metrics is { PromptTokens: 123, CompletionTokens: 24, TokensPerSecond: > 0 }, "Ollama usage was not recorded.");

    await model.ChatAsync([user, answer, new ChatMessage("user", "Continue")]);
    using var request = JsonDocument.Parse(handler.RequestJson);
    JsonElement reply = request.RootElement.GetProperty("messages")[1];
    Assert(reply.EnumerateObject().Count() == 2 && reply.GetProperty("role").GetString() == "assistant", "Metrics leaked into an outgoing chat message.");
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

sealed class RecordingChatModel(params string[] responses) : IChatModel
{
    public List<ChatMessage[]> Requests {get;} = [];
    public Exception? NextFailure {get; set;}

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

sealed class StubHandler : HttpMessageHandler
{
    public string RequestJson {get; private set;} = "";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpContent content = request.Content ?? throw new InvalidOperationException("Expected a request body.");
        RequestJson = await content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { message = new { role = "assistant", content = "Hello!" }, done_reason = "stop", prompt_eval_count = 123, eval_count = 24 })
        };
    }
}

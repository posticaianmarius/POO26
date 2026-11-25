using System.Net;
using System.Text;
using System.Text.Json;
using Zeya.Assistant;
using Zeya.Chat;
using Zeya.Chat.Ollama;
using Zeya.ConsoleUI;
using Zeya.Memory;
using Zeya.Memory.Storage;

if(args.Length is 2 or 3 && args[0] == "--console" && !string.IsNullOrWhiteSpace(args[1]))
{
    ConversationHistory? history = (args.Length == 2 ? "2" : args[2]) switch
    {
        "1" => ConversationHistory.Tier1(),
        "2" => ConversationHistory.Tier2(),
        "3" => ConversationHistory.Tier3(),
        _ => null
    };
    if(history is not null)
    {
        await RunConsoleAsync(args[1], history);
        return;
    }
}

if(args.Length != 0 && !(args.Length == 1 && args[0] == "--live"))
{
    Console.Error.WriteLine("Usage: Zeya.Checks [--live | --console <temporary-memory-path> [1|2|3]]");
    Environment.ExitCode = 2;
    return;
}

string checksDirectory = Path.Combine(Path.GetTempPath(), "ZeyaChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(checksDirectory);
int passed = 0;
Exception? checkFailure = null;

try
{
    var store = new JsonMemoryStore(Path.Combine(checksDirectory, "memory.json"));
    await store.LoadAsync();
    await Check("empty proposals do not create files", async () =>
    {
        var changes = await store.ApplyUpdatesAsync([], "Hello!");
        Assert(changes.Total == 0 && !File.Exists(store.FilePath), "An empty proposal wrote a file.");
    });
    await Check("facts survive a reload", async () =>
    {
        await store.ApplyUpdatesAsync(
            [Add("Prefers short explanations.", "I prefer short explanations.")],
            "I prefer short explanations."
        );

        var reloaded = new JsonMemoryStore(store.FilePath);
        await reloaded.LoadAsync();

        Assert(
            reloaded.Facts.Count == 1
                && reloaded.Facts[0].Evidence == "I prefer short explanations."
                && reloaded.Facts[0].UpdatedAt != default,
            "The saved fact did not survive."
        );
    });
    await Check("exact duplicates do not rewrite the file", async () =>
    {
        string original = await File.ReadAllTextAsync(store.FilePath);
        var changes = await store.ApplyUpdatesAsync(
            [Add("PREFERS  SHORT EXPLANATIONS!", "Keep it short.")],
            "Keep it short."
        );

        Assert(
            changes.Total == 0 && await File.ReadAllTextAsync(store.FilePath) == original,
            "A duplicate changed the file."
        );
    });
    await Check("updates retain IDs and replace facts", async () =>
    {
        int id = store.Facts[0].Id;
        var update = new MemoryUpdate(
            "update",
            id,
            "Prefers detailed explanations.",
            "global",
            "I now prefer detailed explanations."
        );

        var changes = await store.ApplyUpdatesAsync([update], "I now prefer detailed explanations.");

        Assert(
            changes.Updated == 1
                && store.Facts.Count == 1
                && store.Facts[0].Id == id
                && store.Facts[0].Text.Contains("detailed"),
            "The update duplicated or lost the fact."
        );
    });
    await Check("project exceptions preserve global facts", async () =>
    {
        await store.ApplyUpdatesAsync(
            [Add("Prefers short explanations.", "Keep Orion explanations short.", "orion")],
            "Keep Orion explanations short."
        );

        Assert(
            store.Facts.Count == 2
                && store.Facts.Any(entry => entry.Scope == "global")
                && store.Facts.Any(entry => entry.Scope == "orion"),
            "Scopes were merged."
        );

        var scoped = new JsonMemoryStore(Path.Combine(checksDirectory, "scoped.json"));
        await scoped.ApplyUpdatesAsync(
            [Add("Prefers short explanations.", "Keep explanations short.")],
            "Keep explanations short."
        );

        var projectException = new MemoryUpdate(
            "update",
            scoped.Facts[0].Id,
            "Prefers detailed explanations.",
            "orion",
            "Use details for Orion."
        );

        var changes = await scoped.ApplyUpdatesAsync([projectException], "Use details for Orion.");

        Assert(
            changes.Added == 1
                && changes.Updated == 0
                && scoped.Facts.Count == 2
                && scoped.Facts[0].Scope == "global",
            "A cross-scope update replaced a global fact."
        );
    });
    await Check("invalid batches leave disk and memory unchanged", async () =>
    {
        string original = await File.ReadAllTextAsync(store.FilePath);
        MemoryUpdate[] actions =
        [
            Add("Likes blue.", "I like blue."),
            new MemoryUpdate("update", 999, "Unknown fact.", "global", "I like blue.")
        ];

        await ExpectFailure<InvalidDataException>(() => store.ApplyUpdatesAsync(actions, "I like blue."));

        Assert(
            store.Facts.Count == 2 && await File.ReadAllTextAsync(store.FilePath) == original,
            "A rejected batch was partly saved."
        );
    });
    await Check("unsupported evidence is rejected", async () =>
    {
        await ExpectFailure<InvalidDataException>(() => store.ApplyUpdatesAsync(
            [Add("Is an astronaut.", "I am an astronaut.")],
            "Hello!"
        ));
        Assert(store.Facts.Count == 2, "Unsupported evidence was saved.");
    });
    await Check("oversized and repeated changes are rejected", async () =>
    {
        await ExpectFailure<InvalidDataException>(() => store.ApplyUpdatesAsync(
            [Add(new string('x', 241), "Remember this.")],
            "Remember this."
        ));

        int id = store.Facts[0].Id;
        MemoryUpdate[] repeatedDeletions =
        [
            new MemoryUpdate("delete", id, "", "", "Forget it."),
            new MemoryUpdate("delete", id, "", "", "Forget it.")
        ];

        await ExpectFailure<InvalidDataException>(() => store.ApplyUpdatesAsync(repeatedDeletions, "Forget it."));
        Assert(store.Facts.Count == 2, "Invalid changes mutated memory.");
    });
    await Check("forgetting persists without removing other facts", async () =>
    {
        int id = store.Facts[0].Id;
        Assert(await store.ForgetAsync(id), "Existing memory was not removed.");
        Assert(!await store.ForgetAsync(999), "Unknown memory was removed.");
        var reloaded = new JsonMemoryStore(store.FilePath);
        await reloaded.LoadAsync();
        Assert(
            reloaded.Facts.Count == 1 && reloaded.Facts[0].Scope == "orion",
            "Forgetting removed the wrong fact."
        );
    });
    await Check("model deletion uses an existing ID", async () =>
    {
        var deletion = new MemoryUpdate("delete", store.Facts[0].Id, "", "", "Forget Orion.");
        var changes = await store.ApplyUpdatesAsync([deletion], "Forget Orion.");
        var reloaded = new JsonMemoryStore(store.FilePath);
        await reloaded.LoadAsync();
        Assert(changes.Deleted == 1 && reloaded.Facts.Count == 0, "Deletion did not persist.");
    });
    await Check("corrupt files and incomplete records are rejected", async () =>
    {
        string path = Path.Combine(checksDirectory, "corrupt.json");
        await File.WriteAllTextAsync(path, "{broken");
        await ExpectFailure<JsonException>(() => new JsonMemoryStore(path).LoadAsync());
        Assert(await File.ReadAllTextAsync(path) == "{broken", "A corrupt file was overwritten.");
        await File.WriteAllTextAsync(path, "[{\"id\":1,\"text\":\"Fact\",\"scope\":\"global\"}]");
        await ExpectFailure<JsonException>(() => new JsonMemoryStore(path).LoadAsync());
    });
    await Check("failed writes preserve the current memory", async () =>
    {
        string path = Path.Combine(checksDirectory, "blocked.json");
        await File.WriteAllTextAsync(path, "Blocked parent directory.");
        var blocked = new JsonMemoryStore(Path.Combine(path, "memory.json"));
        await ExpectFailure<IOException>(() => blocked.ApplyUpdatesAsync(
            [Add("Likes blue.", "I like blue.")],
            "I like blue."
        ));

        Assert(
            blocked.Facts.Count == 0 && !Directory.EnumerateFiles(checksDirectory, "*.tmp").Any(),
            "A failed save left changes or temporary files."
        );
    });
    await Check("capacity failures preserve saved facts", async () =>
    {
        const int expectedCapacity = 50;
        string path = Path.Combine(checksDirectory, "full.json");
        var facts = Enumerable.Range(1, expectedCapacity)
            .Select(id => new MemoryFact(
                id,
                $"Fact {id}.",
                "global",
                "Test fixture.",
                DateTimeOffset.UtcNow
            ));

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(facts, jsonOptions));
        var full = new JsonMemoryStore(path);
        await full.LoadAsync();
        string original = await File.ReadAllTextAsync(path);
        await ExpectFailure<InvalidDataException>(() => full.ApplyUpdatesAsync(
            [Add("One more fact.", "Remember this.")],
            "Remember this."
        ));

        Assert(
            full.Facts.Count == expectedCapacity && await File.ReadAllTextAsync(path) == original,
            "A full store lost facts."
        );
    });
    await Check("Ollama requests use a schema and reject malformed proposals", async () =>
    {
        using var handler = new StubHandler("{\"actions\":[]}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var manager = new MemoryService(new OllamaChatModel(http, "test"), store);
        Assert((await manager.ExtractAndSaveAsync("Thanks!", [])).Total == 0, "Empty actions were not accepted.");
        using var request = JsonDocument.Parse(handler.RequestBody!);

        JsonElement requestBody = request.RootElement;
        string? schemaType = requestBody.GetProperty("format").GetProperty("type").GetString();
        int temperature = requestBody.GetProperty("options").GetProperty("temperature").GetInt32();
        Assert(schemaType == "object" && temperature == 0, "Structured output was not requested.");

        handler.Content = """
            {"actions":[{"action":"add","id":0,"text":"Likes blue.","scope":"global"}]}
            """;

        await ExpectFailure<JsonException>(() => manager.ExtractAndSaveAsync("I like blue.", []));
        handler.Content = "{\"actions\":null}";
        await ExpectFailure<InvalidDataException>(() => manager.ExtractAndSaveAsync("Thanks!", []));
        handler.Content = "{\"actions\":[],\"unexpected\":true}";
        await ExpectFailure<JsonException>(() => manager.ExtractAndSaveAsync("Thanks!", []));
    });
    await Check("Ollama returns usage metrics without sending them back in history", async () =>
    {
        using var handler = new StubHandler("A measured reply.") { PromptTokens = 123, CompletionTokens = 24 };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var model = new OllamaChatModel(http, "test");
        var firstMessage = new ChatMessage("user", "Hello");
        ChatMessage answer = await model.ChatAsync([firstMessage]);
        ChatMetrics metrics = answer.Metrics ?? throw new InvalidOperationException("Ollama did not attach metrics.");
        Assert(metrics.PromptTokens == 123 && metrics.CompletionTokens == 24 && metrics.Elapsed > TimeSpan.Zero, "Ollama usage was not measured.");
        Assert(metrics.TokensPerSecond == 24 / metrics.Elapsed.TotalSeconds, "The rate did not use request duration.");

        await model.ChatAsync([firstMessage, answer, new ChatMessage("user", "Continue")]);
        using var request = JsonDocument.Parse(handler.RequestBody ?? throw new InvalidOperationException("No second request."));
        JsonElement previousReply = request.RootElement.GetProperty("messages")[1];
        Assert(previousReply.EnumerateObject().Count() == 2, "Metrics were serialized into conversation history.");
    });

    await Check("Qwen counts the latest user message before sending it", async () =>
    {
        TextWriter previousOutput = Console.Out;
        using var output = new StringWriter();
        using var handler = new StubHandler("Hello!")
        {
            RequestReceived = () => Assert(output.ToString().Contains("Chat: input tokens=3" + Environment.NewLine, StringComparison.Ordinal), "The Qwen token count was missing before the request.")
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var model = new OllamaChatModel(http, "qwen3.5:4b-q4_K_M");

        try
        {
            Console.SetOut(output);
            await model.ChatAsync([new ChatMessage("system", "Respond in English."), new ChatMessage("user", "Hello world!")]);
        }
        finally
        {
            Console.SetOut(previousOutput);
        }
    });

    await Check("truncated model responses cannot change memory", async () =>
    {
        using var handler = new StubHandler("{\"actions\":[]}", "length");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var manager = new MemoryService(new OllamaChatModel(http, "test"), store);
        await ExpectFailure<InvalidDataException>(() => manager.ExtractAndSaveAsync("Hello!", []));
    });

    await Check("memory repairs one rejected proposal without partial writes", async () =>
    {
        var chatModel = new RecordingChatModel();
        chatModel.MemoryResponses.Enqueue("""
            {"actions":[{"action":"add","id":0,"text":"Likes blue.","scope":"global","evidence":"User likes blue."}]}
            """);
        chatModel.MemoryResponses.Enqueue("""
            {"actions":[{"action":"add","id":0,"text":"Likes blue.","scope":"global","evidence":"I like blue."}]}
            """);

        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "repaired-memory.json"));
        var memoryService = new MemoryService(chatModel, memoryStore);
        MemoryChangeSummary changes = await memoryService.ExtractAndSaveAsync("I like blue.", []);

        Assert(
            changes.Added == 1
                && memoryStore.Facts.Count == 1
                && memoryStore.Facts[0].Evidence == "I like blue."
                && chatModel.Requests.Count == 2,
            "Repair did not preserve strict evidence validation."
        );
    });

    await Check("assistant rolls back a failed chat request", async () =>
    {
        var chatModel = new RecordingChatModel { FailNextRequest = true };
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "failed-chat.json"));
        var memoryService = new MemoryService(chatModel, memoryStore);
        var assistant = new AssistantSession(chatModel, memoryService);

        await ExpectFailure<HttpRequestException>(() => assistant.ReplyAsync("A failed message."));
        await assistant.ReplyAsync("The next message.");

        ChatMessage[] request = chatModel.Requests[^1];

        Assert(
            request.Length == 2
                && request[0].Role == "system"
                && request[1].Content == "The next message.",
            "A failed request remained in the conversation."
        );
    });

    await Check("assistant saves memory separately from its reply", async () =>
    {
        var chatModel = new RecordingChatModel
        {
            MemoryResponse = """
                {"actions":[{"action":"add","id":0,"text":"Likes blue.","scope":"global","evidence":"I like blue."}]}
                """
        };

        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "assistant-memory.json"));
        var memoryService = new MemoryService(chatModel, memoryStore);
        var assistant = new AssistantSession(chatModel, memoryService);

        AssistantReply reply = await assistant.ReplyAsync("I like blue.");
        Assert(memoryStore.Facts.Count == 0, "Replying unexpectedly changed saved memory.");

        MemoryChangeSummary changes = await assistant.SaveMemoriesAsync(reply);

        Assert(
            changes.Added == 1
                && assistant.SavedFacts.Count == 1
                && chatModel.Requests.Count == 2,
            "The separate memory step did not save the fact."
        );

        Assert(await assistant.ForgetMemoryAsync(assistant.SavedFacts[0].Id), "The fact was not forgotten.");
        await assistant.ReplyAsync("A fresh conversation.");

        ChatMessage[] request = chatModel.Requests[^1];

        Assert(
            assistant.SavedFacts.Count == 0
                && request.Length == 2
                && !request[0].Content.Contains("Likes blue."),
            "Forgetting did not clear saved facts and session context."
        );
    });

    await Check("assistant clears history after a natural-language deletion", async () =>
    {
        var chatModel = new RecordingChatModel();
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "assistant-forget.json"));
        await memoryStore.ApplyUpdatesAsync([Add("Likes blue.", "I like blue.")], "I like blue.");

        var memoryService = new MemoryService(chatModel, memoryStore);
        var assistant = new AssistantSession(chatModel, memoryService);
        await assistant.ReplyAsync("A previous message.");

        chatModel.MemoryResponse = """
            {"actions":[{"action":"delete","id":1,"text":"","scope":"","evidence":"Forget my color."}]}
            """;

        AssistantReply reply = await assistant.ReplyAsync("Forget my color.");
        MemoryChangeSummary changes = await assistant.SaveMemoriesAsync(reply);
        await assistant.ReplyAsync("A new message.");

        Assert(
            changes.Deleted == 1
                && assistant.SavedFacts.Count == 0
                && chatModel.Requests[^1].Length == 2,
            "A deleted fact left earlier conversation context behind."
        );
    });

    await Check("assistant bounds history and memory context", async () =>
    {
        var chatModel = new RecordingChatModel { PromptTokenLimit = 1050 };
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "bounded-chat.json"));
        var assistant = new AssistantSession(chatModel, new MemoryService(chatModel, memoryStore));
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };

        for(int messageNumber = 0; messageNumber < 10; messageNumber ++)
        {
            AssistantReply reply = await assistant.ReplyAsync($"Message {messageNumber}: " + new string('x', 700));

            int contextTokens = reply.MemoryContext.Sum(message => chatModel.EstimateMessageTokens(JsonSerializer.Serialize(message, jsonOptions)));
            Assert(contextTokens <= 512, "Memory extraction exceeded its token budget.");
            if(messageNumber > 0) Assert(reply.MemoryContext.Any(message => message.Content.Length > 600), "A fitting message was cut or discarded.");
        }

        ChatMessage[] request = chatModel.Requests[^1];

        Assert(
            request.Sum(message => chatModel.EstimateMessageTokens(message.Content)) <= chatModel.PromptTokenLimit
                && request[1].Role == "user"
                && !request.Any(message => message.Content.StartsWith("Message 0:")),
            "The chat request was not trimmed to complete exchanges."
        );
    });

    await Check("all three memory tiers retain only their newest complete exchanges", async () =>
    {
        (ConversationHistory History, int Percent)[] tiers =
        [
            (ConversationHistory.Tier1(), 50),
            (ConversationHistory.Tier2(), 75),
            (ConversationHistory.Tier3(), 100)
        ];

        int previousExchanges = 0;
        foreach(var (history, percent) in tiers)
        {
            var model = new RecordingChatModel { PromptTokenLimit = 950 };
            var assistant = new AssistantSession(model, new MemoryService(model, store), history);
            for(int turn = 0; turn <= 12; turn ++)
            {
                await assistant.ReplyAsync($"Turn {turn}".PadRight(320, 'x'));
            }

            ChatMessage[] request = model.Requests[^1];
            int available = model.PromptTokenLimit - model.EstimateMessageTokens(request[0].Content) - model.EstimateMessageTokens(request[^1].Content);
            int exchangeTokens = model.EstimateMessageTokens(request[^1].Content) + model.EstimateMessageTokens("Test reply.");
            int exchanges = available * percent / 100 / exchangeTokens;
            Assert(exchanges > previousExchanges && exchanges < 12, "The tier fixture did not distinguish the token budgets.");
            previousExchanges = exchanges;
            Assert(request.Length == 2 + exchanges * 2 && request[0].Role == "system", "The tier retained the wrong number of exchanges.");
            for(int index = 0; index < exchanges; index ++)
            {
                ChatMessage user = request[index * 2 + 1];
                ChatMessage reply = request[index * 2 + 2];
                Assert(user.Role == "user" && user.Content == $"Turn {12 - exchanges + index}".PadRight(320, 'x') && reply.Role == "assistant", "History did not retain complete exchanges in order.");
            }
            Assert(request[^1].Role == "user" && request[^1].Content == "Turn 12".PadRight(320, 'x'), "The current message was lost.");
        }
    });

    await Check("a chat turn over the token budget preserves history", async () =>
    {
        var model = new RecordingChatModel { PromptTokenLimit = 1000 };
        var assistant = new AssistantSession(model, new MemoryService(model, store));
        await assistant.ReplyAsync("Keep this exchange.");

        await ExpectFailure<InvalidDataException>(() => assistant.ReplyAsync(new string('x', 4000)));
        Assert(model.Requests.Count == 1, "The oversized turn reached the model.");

        await assistant.ReplyAsync("Continue.");
        Assert(model.Requests[^1].Length == 4 && model.Requests[^1][1].Content == "Keep this exchange.", "The oversized turn changed conversation history.");
    });

    await Check("invalid dependencies fail at construction", async () =>
    {
        await ExpectFailure<ArgumentNullException>(() => Task.FromResult(new MemoryService(null!, store)));
        await ExpectFailure<ArgumentNullException>(() => Task.FromResult(new MemoryService(new RecordingChatModel(), null!)));
        await ExpectFailure<ArgumentException>(() => Task.FromResult(new JsonMemoryStore(" ")));
        using var http = new HttpClient();
        await ExpectFailure<ArgumentException>(() => Task.FromResult(new OllamaChatModel(http, " ")));
        await ExpectFailure<ArgumentNullException>(() => Task.FromResult(new OllamaChatModel(null!, "test")));
    });

    await Check("invalid API arguments fail before model or file operations", async () =>
    {
        var chatModel = new RecordingChatModel();
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "bad-arguments.json"));
        var memoryService = new MemoryService(chatModel, memoryStore);
        var assistant = new AssistantSession(chatModel, memoryService);
        await ExpectFailure<ArgumentNullException>(() => memoryStore.ApplyUpdatesAsync(null!, "Hello."));
        await ExpectFailure<ArgumentException>(() => memoryStore.ApplyUpdatesAsync([], " "));
        await ExpectFailure<ArgumentOutOfRangeException>(() => assistant.ForgetMemoryAsync(0));
        await ExpectFailure<ArgumentException>(() => assistant.ReplyAsync(" "));
        await ExpectFailure<ArgumentNullException>(() => assistant.SaveMemoriesAsync(null!));
        await ExpectFailure<ArgumentNullException>(() => memoryService.ExtractAndSaveAsync("Remember this.", null!));
        Assert(chatModel.Requests.Count == 0 && !File.Exists(memoryStore.FilePath), "Bad arguments caused side effects.");

        using var handler = new StubHandler("Ignored.");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        await ExpectFailure<ArgumentException>(() => new OllamaChatModel(http, "test").ChatAsync([]));
        Assert(handler.RequestBody is null, "An invalid request reached HTTP.");
    });

    await Check("broken model contracts fail without repair and roll back chat", async () =>
    {
        var chatModel = new RecordingChatModel { ReplyRole = "user" };
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "broken-contract.json"));
        var memoryService = new MemoryService(chatModel, memoryStore);
        await ExpectFailure<InvalidOperationException>(() => memoryService.ExtractAndSaveAsync("I like blue.", []));
        Assert(chatModel.Requests.Count == 1 && !File.Exists(memoryStore.FilePath), "A contract violation was repaired or saved.");

        var assistant = new AssistantSession(chatModel, memoryService);
        chatModel.ReturnNullReply = true;
        await ExpectFailure<InvalidOperationException>(() => assistant.ReplyAsync("Failed turn."));
        chatModel.ReturnNullReply = false;
        chatModel.ReplyRole = "assistant";
        await assistant.ReplyAsync("Valid turn.");
        Assert(chatModel.Requests[^1].Length == 2, "A broken provider left invalid conversation state.");
    });

    await Check("overlapping turns and clearing pending history fail visibly", async () =>
    {
        var chatModel = new DeferredChatModel();
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "pending-turn.json"));
        var assistant = new AssistantSession(chatModel, new MemoryService(chatModel, memoryStore));
        Task<AssistantReply> pendingReply = assistant.ReplyAsync("First turn.");
        try
        {
            await ExpectFailure<InvalidOperationException>(() => assistant.ReplyAsync("Overlapping turn."));
            await ExpectFailure<InvalidOperationException>(() => assistant.ForgetMemoryAsync(1));
            await ExpectFailure<InvalidOperationException>(() => assistant.SaveMemoriesAsync(new AssistantReply("A prior reply.", "A prior message.", [])));
            await ExpectFailure<InvalidOperationException>(() =>
            {
                assistant.ClearConversation();
                return Task.CompletedTask;
            });
            Assert(chatModel.RequestCount == 1 && !File.Exists(memoryStore.FilePath), "An overlapping operation caused side effects.");
        }
        finally
        {
            chatModel.PendingReply.TrySetResult(new ChatMessage("assistant", "First reply."));
            await pendingReply;
        }
        assistant.ClearConversation();
    });

    await Check("console does not hide programming exceptions", async () =>
    {
        var failure = new InvalidOperationException("Programming error from the test provider.");
        var chatModel = new RecordingChatModel { Failure = failure };
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "visible-error.json"));
        var assistant = new AssistantSession(chatModel, new MemoryService(chatModel, memoryStore));
        TextReader previousInput = Console.In;
        TextWriter previousOutput = Console.Out;
        using var input = new StringReader("Hello\n/exit\n");
        using var output = new StringWriter();
        try
        {
            Console.SetIn(input);
            Console.SetOut(output);
            Exception? observed = null;
            try
            {
                await new ConsoleChat(assistant, memoryStore.FilePath).RunAsync();
            }
            catch(InvalidOperationException error)
            {
                observed = error;
            }
            Assert(ReferenceEquals(observed, failure), "The original programming exception was hidden or replaced.");
            Assert(!output.ToString().Contains("Ollama error"), "A bug was displayed as a network error.");
        }
        finally
        {
            Console.SetIn(previousInput);
            Console.SetOut(previousOutput);
        }
    });

    await Check("internal reply and summary values cannot be invalid", async () =>
    {
        await ExpectFailure<ArgumentException>(() => Task.FromResult(new AssistantReply("", "Hello", [])));
        await ExpectFailure<ArgumentNullException>(() => Task.FromResult(new AssistantReply("Reply", "Hello", null!)));
        await ExpectFailure<ArgumentOutOfRangeException>(() => Task.FromResult(new MemoryChangeSummary(-1, 0, 0)));
        await ExpectFailure<OverflowException>(() => Task.FromResult(new MemoryChangeSummary(int.MaxValue, 1, 0)));
        List<ChatMessage> context = [new ChatMessage("user", "Original")];
        var reply = new AssistantReply("Reply", "Hello", context);
        context.Clear();
        Assert(reply.MemoryContext.Count == 1, "Reply context could be changed through the source list.");
    });

    await Check("memory saves reject foreign stale and consumed replies", async () =>
    {
        var model = new RecordingChatModel();
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "reply-ownership.json"));
        var assistant = new AssistantSession(model, new MemoryService(model, memoryStore));
        AssistantReply oldReply = await assistant.ReplyAsync("First");
        AssistantReply latestReply = await assistant.ReplyAsync("Second");
        await ExpectFailure<InvalidOperationException>(() => assistant.SaveMemoriesAsync(oldReply));
        await ExpectFailure<InvalidOperationException>(() => assistant.SaveMemoriesAsync(new AssistantReply("Reply", "Second", [])));
        Assert(model.Requests.Count == 2, "An invalid reply reached the memory model.");
        await assistant.SaveMemoriesAsync(latestReply);
        await ExpectFailure<InvalidOperationException>(() => assistant.SaveMemoriesAsync(latestReply));
        AssistantReply clearedReply = await assistant.ReplyAsync("Third");
        assistant.ClearConversation();
        await ExpectFailure<InvalidOperationException>(() => assistant.SaveMemoriesAsync(clearedReply));
    });

    await Check("pending memory work excludes all other session operations", async () =>
    {
        var model = new RecordingChatModel();
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "pending-memory.json"));
        var assistant = new AssistantSession(model, new MemoryService(model, memoryStore));
        AssistantReply reply = await assistant.ReplyAsync("Hello");
        var completion = new TaskCompletionSource<ChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        model.PendingMemoryReply = completion;
        Task<MemoryChangeSummary> pending = assistant.SaveMemoriesAsync(reply);
        try
        {
            await ExpectFailure<InvalidOperationException>(() => assistant.ReplyAsync("Overlap"));
            await ExpectFailure<InvalidOperationException>(() => assistant.SaveMemoriesAsync(reply));
            await ExpectFailure<InvalidOperationException>(() => assistant.ForgetMemoryAsync(1));
            await ExpectFailure<InvalidOperationException>(() =>
            {
                assistant.ClearConversation();
                return Task.CompletedTask;
            });
        }
        finally
        {
            completion.TrySetResult(new ChatMessage("assistant", "{\"actions\":[]}"));
            await pending;
        }
        assistant.ClearConversation();
    });

    await Check("store contract errors bypass model repair", async () =>
    {
        var model = new RecordingChatModel();
        var brokenStore = new ContractBreakingMemoryStore();
        var service = new MemoryService(model, brokenStore);
        await ExpectFailure<InvalidOperationException>(() => Task.FromResult(service.GetSavedFactsContext()));
        Assert(model.Requests.Count == 0, "Invalid stored facts reached the model.");
        brokenStore.ExposedFacts = [];
        await ExpectFailure<InvalidOperationException>(() => service.ExtractAndSaveAsync("Hello", []));
        Assert(model.Requests.Count == 1, "A missing store result was treated as repairable model data.");
    });

    await Check("load reports directories and deleted existing memory", async () =>
    {
        string path = Path.Combine(checksDirectory, "load-directory.json");
        Directory.CreateDirectory(path);
        await ExpectFailure<UnauthorizedAccessException>(() => new JsonMemoryStore(path).LoadAsync());
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "deleted-memory.json"));
        await memoryStore.ApplyUpdatesAsync([Add("Likes blue.", "I like blue.")], "I like blue.");
        File.Delete(memoryStore.FilePath);
        await ExpectFailure<FileNotFoundException>(() => memoryStore.LoadAsync());
        Assert(memoryStore.Facts.Count == 1, "A failed reload destroyed in-process memory.");
    });

    await Check("memory store rejects reentrant writes and recovers", async () =>
    {
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "reentrant-store.json"));
        var updates = new ReentrantUpdateList(() =>
        {
            ExpectFailure<InvalidOperationException>(() => memoryStore.ForgetAsync(1)).GetAwaiter().GetResult();
        });
        await memoryStore.ApplyUpdatesAsync(updates, "Hello");
        await memoryStore.ApplyUpdatesAsync([Add("Likes blue.", "I like blue.")], "I like blue.");
        Assert(memoryStore.Facts.Count == 1, "The operation gate was left locked.");
    });

    await Check("invalid provider configuration roles and schemas fail before HTTP", async () =>
    {
        using var noAddress = new HttpClient();
        await ExpectFailure<ArgumentException>(() => Task.FromResult(new OllamaChatModel(noAddress, "test")));
        using var handler = new StubHandler("Unused");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var model = new OllamaChatModel(http, "test");
        await ExpectFailure<ArgumentException>(() => model.ChatAsync([new ChatMessage("unknown", "Hello")]));
        await ExpectFailure<ArgumentException>(() => model.ChatAsync([new ChatMessage("user", "Hello")], default(JsonElement)));
        Assert(handler.RequestBody is null, "A programming error reached HTTP.");
    });

    await Check("console rejects overlapping run loops", async () =>
    {
        var completion = new TaskCompletionSource<ChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new RecordingChatModel { PendingChatReply = completion };
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "overlapping-console.json"));
        var assistant = new AssistantSession(model, new MemoryService(model, memoryStore));
        var console = new ConsoleChat(assistant, memoryStore.FilePath);
        TextReader previousInput = Console.In;
        TextWriter previousOutput = Console.Out;
        using var input = new StringReader("Hello\n/exit\n");
        using var output = new StringWriter();
        try
        {
            Console.SetIn(input);
            Console.SetOut(output);
            Task firstRun = console.RunAsync();
            Exception? overlapFailure = null;
            try
            {
                await console.RunAsync();
            }
            catch(InvalidOperationException error)
            {
                overlapFailure = error;
            }
            finally
            {
                completion.TrySetResult(new ChatMessage("assistant", "Hello."));
            }
            await firstRun;
            Assert(overlapFailure is not null, "A second console loop was allowed.");
        }
        finally
        {
            Console.SetIn(previousInput);
            Console.SetOut(previousOutput);
        }
    });

    await Check("updates cannot replace an obviously unrelated fact", async () =>
    {
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "unrelated-update.json"));
        await memoryStore.ApplyUpdatesAsync([Add("Favorite color is green.", "My favorite color is green.")], "My favorite color is green.");
        string original = await File.ReadAllTextAsync(memoryStore.FilePath);
        var unrelated = new MemoryUpdate("update", 1, "Prefers short explanations.", "global", "I prefer short explanations.");
        await ExpectFailure<InvalidDataException>(() => memoryStore.ApplyUpdatesAsync([unrelated], "I prefer short explanations."));
        Assert(await File.ReadAllTextAsync(memoryStore.FilePath) == original, "An unrelated update overwrote the saved fact.");
    });

    await Check("an unrelated model update is repaired as an addition", async () =>
    {
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "repair-subject.json"));
        await memoryStore.ApplyUpdatesAsync([Add("Favorite color is green.", "My favorite color is green.")], "My favorite color is green.");
        var model = new RecordingChatModel();
        model.MemoryResponses.Enqueue("""
            {"actions":[{"action":"update","id":1,"text":"Prefers short explanations.","scope":"global","evidence":"I prefer short explanations."}]}
            """);
        model.MemoryResponses.Enqueue("""
            {"actions":[{"action":"add","id":0,"text":"Prefers short explanations.","scope":"global","evidence":"I prefer short explanations."}]}
            """);
        var service = new MemoryService(model, memoryStore);
        MemoryChangeSummary changes = await service.ExtractAndSaveAsync("I prefer short explanations.", []);
        Assert(changes.Added == 1 && changes.Updated == 0 && memoryStore.Facts.Count == 2 && memoryStore.Facts[0].Text == "Favorite color is green.", "Repair did not preserve the unrelated original fact.");
    });

    await Check("memory service rejects overlapping direct requests", async () =>
    {
        var completion = new TaskCompletionSource<ChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new RecordingChatModel { PendingMemoryReply = completion };
        var memoryStore = new JsonMemoryStore(Path.Combine(checksDirectory, "memory-service-busy.json"));
        var service = new MemoryService(model, memoryStore);
        Task<MemoryChangeSummary> pending = service.ExtractAndSaveAsync("Hello", []);
        try
        {
            await ExpectFailure<InvalidOperationException>(() => service.ExtractAndSaveAsync("Overlap", []));
            await ExpectFailure<InvalidOperationException>(() => service.ForgetAsync(1));
            Assert(model.Requests.Count == 1, "A concurrent memory request reached the model.");
        }
        finally
        {
            completion.TrySetResult(new ChatMessage("assistant", "{\"actions\":[]}"));
            await pending;
        }
    });

    await Check("toolbox returns local time and date, calculates, and rejects bad requests", () =>
    {
        var toolbox = new Toolbox();
        Assert(TimeOnly.TryParseExact(toolbox.Run("get_time", ""), "HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _), "Expected a local time.");
        DateTime before = DateTime.Today;
        string date = toolbox.Run("get_date", "");
        Assert(date == before.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) || date == DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), "Expected today's date.");
        Assert(toolbox.Run("calculator", "12 * 7") == "84" && toolbox.Run("calculator", "12 + -7") == "5", "Incorrect calculator result.");
        Assert(toolbox.Run("calculator", "10 - 2.5") == "7.5" && toolbox.Run("calculator", "-7 / 2") == "-3.5", "Incorrect decimal calculation.");
        Assert(toolbox.Run("calculator", "2 / 0").Contains("division by zero"), "Division by zero was not handled.");
        Assert(toolbox.Run("calculator", "2 * 3 + 4").StartsWith("Error:") && toolbox.Run("missing", "").StartsWith("Error:"), "Invalid tool inputs should return errors.");
        return Task.CompletedTask;
    });

    await Check("assistant sends tool results back and stores only the final answer", async () =>
    {
        var model = new RecordingChatModel();
        model.ChatResponses.Enqueue("Sure! {\"tool\":\"calculator\",\"input\":\"12 * 7\"}");
        model.ChatResponses.Enqueue("12 times 7 is 84.");
        var assistant = new AssistantSession(model, new MemoryService(model, store));
        AssistantReply reply = await assistant.ReplyAsync("What is 12 times 7?");
        Assert(model.Requests.Count == 2 && model.Requests[1][^1].Content == "Tool result: 84", "The tool result did not reach the model.");
        Assert(reply.Text == "12 times 7 is 84.", "The user did not receive the final answer.");
        await assistant.ReplyAsync("Thanks");
        Assert(model.Requests[^1].Length == 4 && model.Requests[^1][2].Content == reply.Text, "Tool messages leaked into conversation history.");
    });

    await Check("malformed and unknown tool calls are recoverable and count toward the limit", async () =>
    {
        var model = new RecordingChatModel();
        model.ChatResponses.Enqueue("{\"tool\":\"calculator\",\"input\":");
        model.ChatResponses.Enqueue("{\"tool\":\"missing\",\"input\":\"\"}");
        model.ChatResponses.Enqueue("{\"tool\":\"calculator\",\"input\":\"2 / 0\"}");
        model.ChatResponses.Enqueue("{\"tool\":\"get_date\",\"input\":\"\"}");
        var assistant = new AssistantSession(model, new MemoryService(model, store));
        await ExpectFailure<InvalidDataException>(() => assistant.ReplyAsync("An unfinishable tool request"));
        Assert(model.Requests.Count == 4, "The assistant exceeded the tool call limit.");
        Assert(model.Requests[1][^1].Content.Contains("JSON object") && model.Requests[2][^1].Content.Contains("unknown tool") && model.Requests[3][^1].Content.Contains("division by zero"), "Tool errors did not reach the model.");
        await assistant.ReplyAsync("A fresh question");
        Assert(model.Requests[^1].Length == 2, "The failed turn leaked into conversation history.");
    });

    if(args.Contains("--live"))
    {
        await RunLiveChecks();
    }

    Console.WriteLine($"Passed: {passed} checks.");
}
catch(Exception error)
{
    checkFailure = error;
    throw;
}
finally
{
    try
    {
        foreach(string path in Directory.EnumerateFiles(checksDirectory)) File.Delete(path);
        foreach(string path in Directory.EnumerateDirectories(checksDirectory)) Directory.Delete(path);
        Directory.Delete(checksDirectory);
    }
    catch(Exception cleanupError) when(checkFailure is not null)
    {
        throw new AggregateException("Checks failed and cleanup also failed.", checkFailure, cleanupError);
    }
}

async Task Check(string name, Func<Task> test)
{
    try
    {
        await test();
    }
    catch
    {
        Console.Error.WriteLine($"FAIL {name}");
        throw;
    }
    passed ++;
    Console.WriteLine($"PASS {name}");
}

async Task RunLiveChecks()
{
    using var http = new HttpClient
    {
        BaseAddress = new Uri("http://127.0.0.1:11434/"),
        Timeout = TimeSpan.FromMinutes(3)
    };
    var ollama = new OllamaChatModel(http, "qwen3.5:4b-q4_K_M");
    var store = new JsonMemoryStore(Path.Combine(checksDirectory, "live.json"));
    await store.LoadAsync();
    var manager = new MemoryService(ollama, store);
    await Check("live: extract a lasting fact", async () =>
    {
        var changes = await manager.ExtractAndSaveAsync("Remember that my favorite color is blue.", []);
        Assert(
            changes.Added == 1
                && store.Facts.Count == 1
                && store.Facts[0].Text.Contains("blue", StringComparison.OrdinalIgnoreCase),
            "Qwen did not extract the color."
        );
    });
    await Check("live: skip a paraphrased duplicate", async () =>
    {
        var changes = await manager.ExtractAndSaveAsync("Blue is my favourite colour.", []);
        Assert(changes.Total == 0 && store.Facts.Count == 1, "Qwen saved a paraphrase twice.");
    });
    await Check("live: update a corrected fact", async () =>
    {
        int id = store.Facts[0].Id;
        var changes = await manager.ExtractAndSaveAsync("Correction: my favorite color is green now, not blue.", []);
        Assert(
            changes.Updated == 1
                && store.Facts.Count == 1
                && store.Facts[0].Id == id
                && store.Facts[0].Text.Contains("green", StringComparison.OrdinalIgnoreCase),
            "Qwen did not replace the old color."
        );
    });
    await Check("live: skip questions, filler and session-only facts", async () =>
    {
        var fillerChanges = await manager.ExtractAndSaveAsync(
            "Thanks!",
            [new ChatMessage("assistant", "You are probably an astronaut.")]
        );

        var questionChanges = await manager.ExtractAndSaveAsync("What is a class in C#?", []);
        var sessionOnlyChanges = await manager.ExtractAndSaveAsync(
            "Remember that my favorite animal is a fox for this conversation only.",
            []
        );

        Assert(fillerChanges.Total == 0, "An assistant guess was saved.");
        Assert(questionChanges.Total == 0, "A generic question became a memory.");
        Assert(sessionOnlyChanges.Total == 0, "A session-only fact was saved.");
    });
    await Check("live: preserve global and project-specific preferences", async () =>
    {
        await manager.ExtractAndSaveAsync("In general, I prefer short explanations.", []);
        await manager.ExtractAndSaveAsync("For project Orion only, I prefer detailed explanations.", []);
        bool globalPreferenceExists = store.Facts.Any(entry =>
            entry.Scope == "global" && entry.Text.Contains("short", StringComparison.OrdinalIgnoreCase));

        bool projectExceptionExists = store.Facts.Any(entry =>
            entry.Scope.Contains("orion", StringComparison.OrdinalIgnoreCase)
                && entry.Text.Contains("detailed", StringComparison.OrdinalIgnoreCase));

        Assert(
            store.Facts.Count == 3 && globalPreferenceExists && projectExceptionExists,
            "A scoped exception replaced a global preference."
        );
    });
    await Check("live: recall a fact after reloading the file", async () =>
    {
        var reloaded = new JsonMemoryStore(store.FilePath);
        await reloaded.LoadAsync();
        var memory = new MemoryService(ollama, reloaded);
        var assistant = new AssistantSession(ollama, memory);
        AssistantReply answer = await assistant.ReplyAsync("What is my favorite color? Reply with just the color.");

        Assert(
            answer.Text.Contains("green", StringComparison.OrdinalIgnoreCase)
                && !answer.Text.Contains("blue", StringComparison.OrdinalIgnoreCase),
            "The reloaded memory was not recalled correctly."
        );
    });
    await Check("live: forget a fact on explicit request", async () =>
    {
        var changes = await manager.ExtractAndSaveAsync("Forget my favorite color.", []);
        bool colorFactExists = store.Facts.Any(entry =>
            entry.Text.Contains("color", StringComparison.OrdinalIgnoreCase));

        Assert(
            changes.Deleted == 1 && store.Facts.Count == 2 && !colorFactExists,
            "The requested fact was not forgotten."
        );
    });
}
static async Task RunConsoleAsync(string memoryFilePath, ConversationHistory history)
{
    Console.InputEncoding = Encoding.UTF8;
    Console.OutputEncoding = Encoding.UTF8;
    var memoryStore = new JsonMemoryStore(memoryFilePath);

    try
    {
        await memoryStore.LoadAsync();
    }
    catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
    {
        Console.WriteLine($"Cannot load memory: {error.Message}");
        Console.WriteLine(memoryStore.FilePath);
        Environment.ExitCode = 1;
        return;
    }

    using var httpClient = new HttpClient { BaseAddress = AssistantSettings.OllamaAddress, Timeout = AssistantSettings.RequestTimeout };
    IChatModel chatModel = new OllamaChatModel(httpClient, AssistantSettings.ModelName);
    var memoryService = new MemoryService(chatModel, memoryStore);
    var assistant = new AssistantSession(chatModel, memoryService, history);
    var consoleChat = new ConsoleChat(assistant, memoryStore.FilePath);
    await consoleChat.RunAsync();
}

static MemoryUpdate Add(string text, string evidence, string scope = "global")
{
    return new MemoryUpdate("add", 0, text, scope, evidence);
}

static void Assert(bool condition, string message)
{
    if(!condition)
    {
        throw new Exception(message);
    }
}

static async Task ExpectFailure<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
    }
    catch(T)
    {
        return;
    }

    throw new Exception($"Expected {typeof(T).Name}.");
}

sealed class StubHandler(string content, string doneReason = "stop") : HttpMessageHandler
{
    public string Content { get; set; } = content;
    public string? RequestBody { get; private set; }
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public Action? RequestReceived { get; init; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestReceived?.Invoke();
        HttpContent content = request.Content ?? throw new InvalidOperationException("The stub expected a JSON request body.");
        RequestBody = await content.ReadAsStringAsync(cancellationToken);
        var response = new
        {
            message = new
            {
                role = "assistant",
                content = Content
            },
            done_reason = doneReason,
            prompt_eval_count = PromptTokens,
            eval_count = CompletionTokens
        };

        string json = JsonSerializer.Serialize(response);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}

sealed class RecordingChatModel : IChatModel
{
    public int PromptTokenLimit { get; init; } = 7168;
    public int EstimateMessageTokens(string text) => (text.Length + 3) / 4;
    public string ReplyRole { get; set; } = "assistant";
    public bool ReturnNullReply { get; set; }
    public Exception? Failure { get; set; }
    public bool FailNextRequest { get; set; }
    public string MemoryResponse { get; set; } = "{\"actions\":[]}";
    public Queue<string> MemoryResponses { get; } = new();
    public Queue<string> ChatResponses { get; } = new();
    public TaskCompletionSource<ChatMessage>? PendingMemoryReply { get; set; }
    public TaskCompletionSource<ChatMessage>? PendingChatReply { get; set; }
    public List<ChatMessage[]> Requests { get; } = [];

    public Task<ChatMessage> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        JsonElement? responseSchema = null)
    {
        Requests.Add(messages.ToArray());
        if(Failure is not null) return Task.FromException<ChatMessage>(Failure);
        if(ReturnNullReply) return Task.FromResult<ChatMessage>(null!);
        if(responseSchema.HasValue && PendingMemoryReply is not null) return PendingMemoryReply.Task;
        if(!responseSchema.HasValue && PendingChatReply is not null) return PendingChatReply.Task;

        if(FailNextRequest)
        {
            FailNextRequest = false;
            return Task.FromException<ChatMessage>(new HttpRequestException("Test request failed."));
        }

        string content = responseSchema.HasValue
            ? (MemoryResponses.TryDequeue(out string? memoryResponse) ? memoryResponse : MemoryResponse)
            : (ChatResponses.TryDequeue(out string? chatResponse) ? chatResponse : "Test reply.");

        return Task.FromResult(new ChatMessage(ReplyRole, content));
    }
}

sealed class DeferredChatModel : IChatModel
{
    public int PromptTokenLimit => 7168;
    public int EstimateMessageTokens(string text) => (text.Length + 3) / 4;
    public int RequestCount { get; private set; }
    public TaskCompletionSource<ChatMessage> PendingReply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ChatMessage> ChatAsync(IReadOnlyList<ChatMessage> messages, JsonElement? responseSchema = null)
    {
        RequestCount ++;
        return PendingReply.Task;
    }
}

sealed class ContractBreakingMemoryStore : IMemoryStore
{
    public IReadOnlyList<MemoryFact>? ExposedFacts { get; set; }
    public IReadOnlyList<MemoryFact> Facts => ExposedFacts!;
    public Task LoadAsync() => Task.CompletedTask;
    public Task<bool> ForgetAsync(int memoryId) => Task.FromResult(false);
    public Task<MemoryChangeSummary> ApplyUpdatesAsync(IReadOnlyList<MemoryUpdate> updates, string userMessage) => Task.FromResult<MemoryChangeSummary>(null!);
}

sealed class ReentrantUpdateList(Action onCount) : IReadOnlyList<MemoryUpdate>
{
    public int Count
    {
        get
        {
            onCount();
            return 0;
        }
    }
    public MemoryUpdate this[int index] => throw new ArgumentOutOfRangeException(nameof(index));
    public IEnumerator<MemoryUpdate> GetEnumerator() => Enumerable.Empty<MemoryUpdate>().GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

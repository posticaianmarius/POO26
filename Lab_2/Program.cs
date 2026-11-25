
using System.Text;
using System.Text.Json;
using Zeya.Assistant;
using Zeya.Chat;
using Zeya.Chat.Ollama;
using Zeya.ConsoleUI;
using Zeya.Memory;
using Zeya.Memory.Storage;

// Encoding
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

if(!ConsoleOptions.TryRead(out int memoryTier, out Logger.LogLevel logLevel)) return;

Logger.SetLevel(logLevel);

// Select history budget
int historyBudgetPercent = memoryTier switch
{
    1 => 50,
    2 => 75,
    3 => 100,
    _ => 50
};

var history = new ConversationHistory();
var memoryStore = new JsonMemoryStore();

try {await memoryStore.LoadAsync();}
catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
{
    Console.WriteLine($"Cannot load memory: {error.Message}");
    Console.WriteLine(memoryStore.FilePath);
    Environment.ExitCode = 1;
    return;
}

using var httpClient = new HttpClient
{
    BaseAddress = AssistantSettings.OllamaAddress,
    Timeout = AssistantSettings.RequestTimeout
};

IChatModel chatModel = new OllamaChatModel(httpClient, AssistantSettings.ModelName);

var memoryService = new MemoryService(chatModel, memoryStore);
var assistant = new AssistantSession(chatModel, memoryService, history, historyBudgetPercent);
var consoleChat = new ConsoleChat(assistant, memoryStore.FilePath);

await consoleChat.RunAsync();

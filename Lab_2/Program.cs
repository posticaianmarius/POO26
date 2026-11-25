
using System.Text;
using Zeya.Assistant;
using Zeya.Chat.Ollama;
using Zeya.ConsoleUI;

// Encoding
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

if(!ConsoleOptions.TryRead(out int memoryTier, out Logger.LogLevel logLevel)) return;

Logger.SetLevel(logLevel);

ConversationHistory history = memoryTier switch
{
    1 => ConversationHistory.Tier1(),
    2 => ConversationHistory.Tier2(),
    3 => ConversationHistory.Tier3(),
    _ => throw new InvalidOperationException("Invalid memory tier.")
};

using var httpClient = new HttpClient
{
    BaseAddress = AssistantSettings.OllamaAddress,
    Timeout = AssistantSettings.RequestTimeout
};

IChatModel chatModel = new OllamaChatModel(httpClient, AssistantSettings.ModelName);
var assistant = new AssistantSession(chatModel, history);
var consoleChat = new ConsoleChat(assistant);

await consoleChat.RunAsync();

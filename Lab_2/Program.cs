using System.Text;

// Configure console encoding
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

// Read user settings
int[] tierPercentages = [50, 75, 100];
if(!ConsoleOptions.TryRead(tierPercentages, out int memoryTier, out Logger.LogLevel logLevel)) return;

Logger.SetLevel(logLevel);

// Initialize conversation history
var history = new ConversationHistory();

// Configure HTTP client
using var httpClient = new HttpClient
{
    BaseAddress = AssistantSettings.OllamaAddress,
    Timeout = AssistantSettings.RequestTimeout
};

// Initialize assistant
IChatModel chatModel = new OllamaChatModel(httpClient, AssistantSettings.ModelName);
AssistantSession assistant = new AssistantSession(chatModel, history, tierPercentages[memoryTier - 1]);
ConsoleChat consoleChat = new ConsoleChat(assistant);

// Start chat loop
await consoleChat.RunAsync();
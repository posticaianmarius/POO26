public class ConsoleChat
{
    // Input limits and commands
    private const int MaximumInputLength = 4000;
    private const string ClearCommand = "/clear";
    private const string ExitCommand = "/exit";

    private readonly Logger logger = new("ConsoleChat");
    private readonly AssistantSession assistant;

    public ConsoleChat(AssistantSession assistant) => this.assistant = assistant;

    // Run chat loop
    public async Task RunAsync()
    {
        Console.WriteLine($"Zeya | {ClearCommand} {ExitCommand} quit");

        while(true)
        {
            Console.Write("You: ");
            string? input = Console.ReadLine()?.Trim();

            // Handle exit and commands
            if(input is null || input.Equals(ExitCommand, StringComparison.OrdinalIgnoreCase) || input.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;
            if(input.Length == 0 || HandleCommand(input)) continue;

            // Check input length
            if(input.Length > MaximumInputLength)
            {
                logger.Log(Logger.LogLevel.Warn, "Input length exceeded.");
                Console.WriteLine("Use at most 4,000 characters.\n");
                continue;
            }

            await WriteReplyAsync(input);
        }
    }

    // Process console commands
    private bool HandleCommand(string input)
    {
        if(!input.StartsWith('/')) return false;
        if(input.Equals(ClearCommand, StringComparison.OrdinalIgnoreCase))
        {
            assistant.ClearConversation();
            Console.WriteLine("Chat cleared.\n");
        }
        else
        {
            logger.Log(Logger.LogLevel.Warn, $"Unknown command: {input}");
            Console.WriteLine("Unknown command.\n");
        }

        return true;
    }

    // Generate and display reply
    private async Task WriteReplyAsync(string input)
    {
        string reply = await assistant.ReplyAsync(input);
        Console.WriteLine($"AI: {reply}\n");
    }
}
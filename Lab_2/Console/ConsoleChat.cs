
using System.Net;
using System.Text.Json;
using Zeya.Assistant;

namespace Zeya.ConsoleUI;

public sealed class ConsoleChat
{
    private const int MaximumInputLength = 4000;
    private const string ClearCommand = "/clear";
    private const string ExitCommand = "/exit";

    private readonly AssistantSession assistant;
    public ConsoleChat(AssistantSession assistant)
    {
        ArgumentNullException.ThrowIfNull(assistant);
        this.assistant = assistant;
    }

    public async Task RunAsync()
    {
        Console.WriteLine($"Zeya | {ClearCommand} {ExitCommand} quit");

        while(true)
        {
            Console.Write("You: ");
            string? input = Console.ReadLine()?.Trim();

            if(input is null || input.Equals(ExitCommand, StringComparison.OrdinalIgnoreCase) || input.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;
            if(input.Length == 0 || HandleCommand(input)) continue;
            if(input.Length > MaximumInputLength)
            {
                Console.WriteLine("Use at most 4,000 characters.\n");
                continue;
            }

            await WriteReplyAsync(input);
        }
    }

    private bool HandleCommand(string input)
    {
        if(!input.StartsWith('/')) return false;
        if(input.Equals(ClearCommand, StringComparison.OrdinalIgnoreCase))
        {
            assistant.ClearConversation();
            Console.WriteLine("Chat cleared.\n");
        }
        else Console.WriteLine("Unknown command.\n");

        return true;
    }

    private async Task WriteReplyAsync(string input)
    {
        try
        {
            string reply = await assistant.ReplyAsync(input);
            Console.WriteLine($"AI: {reply}\n");
        }
        catch(HttpRequestException error)
        {
            Console.WriteLine(error.StatusCode == HttpStatusCode.NotFound ? "Model not found.\n" : "Ollama unavailable.\n");
        }
        catch(OperationCanceledException)
        {
            Console.WriteLine("Ollama timed out.\n");
        }
        catch(Exception error) when(error is JsonException or InvalidDataException or IOException)
        {
            Console.WriteLine($"Could not read reply: {error.Message}\n");
        }
    }
}

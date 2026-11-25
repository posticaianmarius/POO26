
using System.Net;
using System.Text.Json;
using Zeya.Assistant;
using Zeya.Memory;

namespace Zeya.ConsoleUI;

public sealed class ConsoleChat
{
    private const int MaximumInputLength = 4000;
    private const string MemoryCommand = "/memory";
    private const string ForgetCommand = "/forget";
    private const string ClearCommand = "/clear";
    private const string ExitCommand = "/exit";

    private readonly AssistantSession assistant;
    private readonly string memoryFilePath;

    public ConsoleChat(AssistantSession assistant, string memoryFilePath)
    {
        this.assistant = assistant;
        this.memoryFilePath = memoryFilePath;
    }

    public async Task RunAsync() => await RunLoopAsync();

    private async Task RunLoopAsync()
    {
        Console.WriteLine($"Zeya | {MemoryCommand} {ForgetCommand} <id> {ClearCommand} {ExitCommand}");

        while(true)
        {
            Console.Write("You: ");
            string? input = Console.ReadLine()?.Trim();

            if(input is null || input.Equals(ExitCommand, StringComparison.OrdinalIgnoreCase)) break;
            else if(input.Length == 0) continue;
            else if(await HandleCommandAsync(input)) continue;
            else if(input.Length > MaximumInputLength) {Console.WriteLine("Keep messages under 4,000 characters.\n"); continue;}

            AssistantReply? reply = await WriteReplyAsync(input);

            if(reply is not null) await SaveMemoriesAsync(reply);
        }
    }

    private async Task<bool> HandleCommandAsync(string input)
    {
        if(!input.StartsWith('/')) return false;
        else if(input.Equals(ClearCommand, StringComparison.OrdinalIgnoreCase))
        {
            assistant.ClearConversation();
            Console.WriteLine("Chat cleared. Memories kept.\n");
        }
        else if(input.Equals(MemoryCommand, StringComparison.OrdinalIgnoreCase)) ShowMemories();
        else if(input.Equals(ForgetCommand, StringComparison.OrdinalIgnoreCase) || input.StartsWith(ForgetCommand + " ", StringComparison.OrdinalIgnoreCase)) await ForgetMemoryAsync(input);
        else Console.WriteLine("Unknown command.\n");

        return true;
    }
    private async Task<AssistantReply?> WriteReplyAsync(string input)
    {
        AssistantReply reply = await assistant.ReplyAsync(input);
        Console.WriteLine($"AI: {reply.Text}\n");
        return reply;
    }
    private void ShowMemories()
    {
        Console.WriteLine(memoryFilePath);

        if(assistant.SavedFacts.Count == 0) Console.WriteLine("No memories.");
        foreach(MemoryFact memory in assistant.SavedFacts) Console.WriteLine($"{memory.Id}. [{memory.Scope}] {memory.Text}");

        Console.WriteLine();
    }
    private async Task SaveMemoriesAsync(AssistantReply reply)
    {
        MemoryChangeSummary changes = await assistant.SaveMemoriesAsync(reply);

        if(changes.Total > 0) Console.WriteLine("Memory saved.\n");
    }
    private async Task ForgetMemoryAsync(string input)
    {
        string idArgument = input[ForgetCommand.Length..].Trim();

        if(!int.TryParse(idArgument, out int memoryId) || memoryId <= 0) {Console.WriteLine($"Use {ForgetCommand} <id>.\n"); return;}

        bool removed = await assistant.ForgetMemoryAsync(memoryId);
        Console.WriteLine(removed ? "Forgotten. Chat cleared.\n" : "Memory not found.\n");
    }
}

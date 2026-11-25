
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Zeya.Chat.Ollama;

public sealed class OllamaChatModel : IChatModel
{
    // Token limits
    private const int ContextTokenLimit = 8192;
    private const int ReplyTokenLimit = 1024;
    private const int PromptFramingTokens = 32;
    private const int MessageFramingTokens = 16;

    private readonly HttpClient httpClient;
    private readonly string modelName;
    private readonly ModelTokenCounter tokenCounter;
    private readonly Logger logger = new("Ollama");

    public int PromptTokenLimit => ContextTokenLimit - ReplyTokenLimit - PromptFramingTokens;

    public OllamaChatModel(HttpClient httpClient, string modelName)
    {
        this.httpClient = httpClient;
        this.modelName = modelName;
        tokenCounter = new ModelTokenCounter(modelName);
    }

    // Estimate token usage
    public int EstimateMessageTokens(string text) => tokenCounter.Count(text) + MessageFramingTokens;

    // Request AI response
    public async Task<ChatMessage> ChatAsync(IReadOnlyList<ChatMessage> messages, JsonElement? responseSchema = null)
    {
        var request = new
        {
            model = modelName,
            messages,
            stream = false,
            think = false,
            keep_alive = "5m",
            format = responseSchema,
            options = new
            {
                num_ctx = ContextTokenLimit,
                num_predict = ReplyTokenLimit,
                temperature = responseSchema.HasValue ? 0 : 0.7
            }
        };

        string requestKind = responseSchema.HasValue ? "Memory" : "Chat";

        if(!responseSchema.HasValue && messages.Count > 0 && messages[^1].Role == "user")
        {
            int? inputTokens = tokenCounter.CountExact(messages[^1].Content);
            string count = inputTokens?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
            logger.Log(Logger.LogLevel.Info, $"Chat: input tokens={count}");
        }

        logger.Log(Logger.LogLevel.Trace, $"{requestKind}: sending {messages.Count} messages.");

        var timer = Stopwatch.StartNew();

        try
        {
            using var response = await httpClient.PostAsJsonAsync("api/chat", request);
            response.EnsureSuccessStatusCode();

            var reply = await response.Content.ReadFromJsonAsync<OllamaChatResponse>();
            timer.Stop();

            // Validate external response
            if(reply?.Message is not { Role: "assistant" } answer || string.IsNullOrWhiteSpace(answer.Content)) throw new InvalidDataException("Invalid reply.");
            if(reply.DoneReason == "length") throw new InvalidDataException("Reply exceeded its limit.");

            // Record performance metrics
            var metrics = new ChatMetrics(reply.PromptTokens, reply.CompletionTokens, timer.Elapsed);
            string prompt = metrics.PromptTokens?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
            string completion = metrics.CompletionTokens?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
            string rate = metrics.TokensPerSecond?.ToString("F2", CultureInfo.InvariantCulture) ?? "n/a";
            string seconds = metrics.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);

            logger.Log(Logger.LogLevel.Info, $"{requestKind}: prompt={prompt} completion={completion} elapsed={seconds}s tokens/sec={rate}");

            return answer with { Metrics = metrics };
        }
        catch(Exception error) when(error is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException or IOException)
        {
            logger.Log(Logger.LogLevel.Error, $"{requestKind} request failed: {error.Message}");
            throw;
        }
    }
}

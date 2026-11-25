
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

    private readonly HttpClient httpClient;
    private readonly string modelName;
    private readonly Logger logger = new("Ollama");

    public OllamaChatModel(HttpClient httpClient, string modelName)
    {
        this.httpClient = httpClient;
        this.modelName = modelName;
    }

    // Request AI response
    public async Task<ChatMessage> ChatAsync(IReadOnlyList<ChatMessage> messages)
    {
        var request = new
        {
            model = modelName,
            messages,
            stream = false,
            think = false,
            keep_alive = "5m",
            options = new
            {
                num_ctx = ContextTokenLimit,
                num_predict = ReplyTokenLimit,
                temperature = 0.7
            }
        };

        logger.Log(Logger.LogLevel.Trace, $"Chat: sending {messages.Count} messages.");

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

            logger.Log(Logger.LogLevel.Info, $"Chat: prompt={prompt} completion={completion} elapsed={seconds}s tokens/sec={rate}");

            return answer with { Metrics = metrics };
        }
        catch(Exception error) when(error is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException or IOException)
        {
            logger.Log(Logger.LogLevel.Error, $"Chat request failed: {error.Message}");
            throw;
        }
    }
}

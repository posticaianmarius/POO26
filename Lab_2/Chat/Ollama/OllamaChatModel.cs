using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

public class OllamaChatModel : IChatModel
{
    // Token limits
    private const int ContextTokenLimit = 8192;
    private const int ReplyTokenLimit = 1024;
    private const int PromptFramingTokens = 32;
    private const int MessageFramingTokens = 16;

    private readonly HttpClient httpClient;
    private readonly string modelName;
    private readonly ModelTokenCounter tokenCounter;
    private readonly Logger logger = new("OllamaChatModel");

    // Available prompt capacity
    public int PromptTokenLimit => ContextTokenLimit - ReplyTokenLimit - PromptFramingTokens;

    // Initialize model and tokenizer
    public OllamaChatModel(HttpClient httpClient, string modelName)
    {
        this.httpClient = httpClient;
        this.modelName = modelName;
        tokenCounter = new ModelTokenCounter(modelName);
    }

    // Estimate message tokens
    public int EstimateMessageTokens(string text) => tokenCounter.Count(text) + MessageFramingTokens;

    // Request AI response
    public async Task<ChatMessage> ChatAsync(IReadOnlyList<ChatMessage> messages)
    {
        // Configure request
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
                temperature = 0.5
            }
        };

        logger.Log(Logger.LogLevel.Trace, $"Sending {messages.Count} messages to {modelName}.");

        // Measure request duration
        Stopwatch timer = Stopwatch.StartNew();

        // Send HTTP request
        using var response = await httpClient.PostAsJsonAsync("api/chat", request);

        if(!response.IsSuccessStatusCode) logger.Log(Logger.LogLevel.Error, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        response.EnsureSuccessStatusCode();

        // Deserialize JSON response
        var reply = (await response.Content.ReadFromJsonAsync<OllamaChatResponse>())!;

        timer.Stop();

        // Collect performance metrics
        var metrics = new ChatMessage.ChatMetrics(reply.PromptTokens, reply.CompletionTokens, timer.Elapsed);

        // Format metrics for logging
        string promptTokens = metrics.PromptTokens?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
        string completionTokens = metrics.CompletionTokens?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
        string tokensPerSecond = metrics.TokensPerSecond?.ToString("F2", CultureInfo.InvariantCulture) ?? "n/a";
        string elapsedSeconds = metrics.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);

        logger.Log(Logger.LogLevel.Info, $"Prompt: {promptTokens}, Completion: {completionTokens}, Duration: {elapsedSeconds}s, Speed: {tokensPerSecond} tokens/s");

        // Return response with metrics
        return reply.Message! with {Metrics = metrics};
    }

    // Ollama JSON response structure
    record OllamaChatResponse
    (
        [property: JsonPropertyName("message")] ChatMessage? Message,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptTokens,
        [property: JsonPropertyName("eval_count")] int? CompletionTokens
    );
}
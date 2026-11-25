static class TokenizerRegistry
{
    // Registered models and tokenizer paths
    private static readonly Dictionary<string, string> tokenizerPaths = new(StringComparer.Ordinal)
    {
        ["qwen3.5:4b-q4_K_M"] = "Chat/Ollama/Qwen3.5-4B/tokenizer.json"
    };

    // Get tokenizer path by model name
    public static string? GetPath(string modelName) => tokenizerPaths.GetValueOrDefault(modelName);
}
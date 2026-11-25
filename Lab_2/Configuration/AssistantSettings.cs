public static class AssistantSettings
{
    // Default AI model
    public const string ModelName = "qwen3.5:4b-q4_K_M";

    // Ollama connection settings
    public static Uri OllamaAddress {get;} = new("http://127.0.0.1:11434/");
    public static TimeSpan RequestTimeout {get;} = TimeSpan.FromMinutes(3);
}
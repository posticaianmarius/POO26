using System.Text;
using Tokenizer = Tokenizers.HuggingFace.Tokenizer.Tokenizer;

class ModelTokenCounter
{
    private readonly Tokenizer? tokenizerInstance;
    private readonly Logger logger = new("ModelTokenCounter");

    // Load model-specific tokenizer
    public ModelTokenCounter(string modelName)
    {
        string? tokenizerPath = TokenizerRegistry.GetPath(modelName);

        if(tokenizerPath is null)
        {
            logger.Log(Logger.LogLevel.Warn, "Tokenizer unavailable. Using UTF-8 fallback.");
            return;
        }

        string path = Path.Combine(AppContext.BaseDirectory, tokenizerPath);
        tokenizerInstance = Tokenizer.FromFile(path);

        logger.Log(Logger.LogLevel.Trace, $"Tokenizer loaded: {modelName}.");
    }

    // Count tokens with UTF-8 fallback
    public int Count(string text) => CountExact(text) ?? Encoding.UTF8.GetByteCount(text);

    // Count exact tokens
    int? CountExact(string text)
    {
        if(tokenizerInstance is null) return null;
        return tokenizerInstance.Encode(text, addSpecialTokens: false).Single().Ids.Count;
    }
}
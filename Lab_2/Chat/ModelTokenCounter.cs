using System.Text;
using Tokenizer = Tokenizers.HuggingFace.Tokenizer.Tokenizer;

internal sealed class ModelTokenCounter
{
    private readonly Tokenizer? tokenizerInstance;

    // Load model-specific tokenizer
    public ModelTokenCounter(string modelName)
    {
        string? tokenizerPath = TokenizerRegistry.GetPath(modelName);

        if(tokenizerPath is null) return;

        string path = Path.Combine(AppContext.BaseDirectory, tokenizerPath);
        tokenizerInstance = Tokenizer.FromFile(path);
    }

    // Count tokens with UTF-8 fallback
    public int Count(string text) => CountExact(text) ?? Encoding.UTF8.GetByteCount(text);

    // Count exact tokens if tokenizer is available
    public int? CountExact(string text)
    {
        if(tokenizerInstance is null) return null;
        return tokenizerInstance.Encode(text, addSpecialTokens: false).Single().Ids.Count;
    }
}
static class ConsoleOptions
{
    // Default values
    private const int DefaultMemoryTier = 2;
    private const Logger.LogLevel DefaultLogLevel = Logger.LogLevel.Info;

    // Read console options
    public static bool TryRead(IReadOnlyList<int> tierPercentages, out int memoryTier, out Logger.LogLevel logLevel)
    {
        memoryTier = DefaultMemoryTier;
        logLevel = DefaultLogLevel;

        // Read memory tier
        while(true)
        {
            Console.Write($"History token budget (1 = {tierPercentages[0]}%, 2 = {tierPercentages[1]}%, 3 = {tierPercentages[2]}%) [{DefaultMemoryTier}]: ");

            string? input = Console.ReadLine()?.Trim();
            if(input is null) return false;
            else if(input.Length == 0) break;
            else if(input is "1" or "2" or "3") { memoryTier = int.Parse(input); break; }

            Console.WriteLine("Enter 1, 2, or 3.");
        }

        // Read log level
        while(true)
        {
            Console.Write($"Log level (trace, info, warn, error) [{DefaultLogLevel.ToString().ToLower()}]: ");

            string? input = Console.ReadLine()?.Trim();
            if(input is null) return false;
            if(input.Length == 0) return true;
            if(TryParseLogLevel(input, out Logger.LogLevel selectedLevel)) { logLevel = selectedLevel; return true; }

            Console.WriteLine("Enter trace, info, warn, or error.");
        }
    }

    // Validate log level
    private static bool TryParseLogLevel(string value, out Logger.LogLevel level) => Enum.TryParse(value, ignoreCase: true, out level) && Enum.IsDefined(level);
}

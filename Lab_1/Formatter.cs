using System.Text.RegularExpressions;

class Formatter
{
    // Random messages by log level
    private static readonly string[][] randomMessages =
    {
        new[] { "Checking input", "Processing data" },
        new[] { "Operation completed", "Everything looks good" },
        new[] { "Check the input", "Something unexpected" },
        new[] { "Operation failed", "Invalid data" }
    };

    // Seted template
    private readonly string template;

    // Set format template
    public Formatter(string template = "[%t] [%l] %n: %m")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        this.template = template;
    }

    // Format log message
    public string Format(string name, Logger.LogLevel level, string message)
    {
        string time = DateTime.Now.ToString("HH:mm:ss");
        string randomMessage = "";
        if(template.Contains("%r"))
        {
            string[] messages = randomMessages[(int)level];
            randomMessage = messages[Random.Shared.Next(messages.Length)];
        }

        // Replace template tokens
        return Regex.Replace(template, "%[tlnmr]", match => match.Value switch
        {
            "%t" => time,
            "%l" => level.ToString().ToUpper(),
            "%n" => name,
            "%m" => message,
            "%r" => randomMessage,
            _ => ""
        });
    }
}

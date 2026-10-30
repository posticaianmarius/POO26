class Logger
{
    public enum LogLevel {Trace = 0, Info = 1, Warn = 2, Error = 3}
    private static string format = "[%t] [%l] %n: %m";
    private static LogLevel level = LogLevel.Info;
    private static readonly string[][] randomMessages =
    {
        new[] { "Checking input", "Processing data" },
        new[] { "Operation completed", "Everything looks good" },
        new[] { "Check the input", "Something unexpected" },
        new[] { "Operation failed", "Invalid data" }
    };

    private readonly string name;
    
    public Logger(string name) => this.name = name;
    public static void SetLevel(LogLevel level) => Logger.level = level;
    public static void SetFormat(string format) => Logger.format = format;

    public void Log(LogLevel level, string message)
    {
        if(level < Logger.level) return;

        string output = format.Replace("%t", DateTime.Now.ToString("HH:mm:ss"));
        output = output.Replace("%l", level.ToString().ToUpperInvariant());
        output = output.Replace("%n", name);
        output = output.Replace("%m", message);

        if(format.Contains("%r"))
        {
            string[] messages = randomMessages[(int)level];
            output = output.Replace("%r", messages[Random.Shared.Next(messages.Length)]);
        }

        Console.WriteLine(output);
    }
}
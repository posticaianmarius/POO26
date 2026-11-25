class Logger
{
    // Log levels
    public enum LogLevel {Trace = 0, Info = 1, Warn = 2, Error = 3}

    // Logger settings
    private static LogLevel level = LogLevel.Info;
    private readonly string name;
    private readonly Formatter formatter;

    // Default constructor
    public Logger(string name) : this(name, new Formatter()) {}

    // Constructor with a supplied formatter
    public Logger(string name, Formatter formatter)
    {
        this.name = name;
        this.formatter = formatter;
    }

    // Set minimum log level
    public static void SetLevel(LogLevel level) => Logger.level = level;

    // Write log if level is sufficient
    public void Log(LogLevel level, string message)
    {
        if(level < Logger.level) return;
        Console.WriteLine(formatter.Format(name, level, message));
    }
}

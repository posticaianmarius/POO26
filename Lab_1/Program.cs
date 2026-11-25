
Logger.SetLevel(Logger.LogLevel.Trace);

var defaultFormatter = new Formatter();
var shortFormatter = new Formatter("[%l] %m");
Console.WriteLine(defaultFormatter.Format("Demo", Logger.LogLevel.Info, "Ratio is 100%n"));
Console.WriteLine(shortFormatter.Format("Demo", Logger.LogLevel.Info, "Short format"));

var logger = new Logger("Demo", shortFormatter);
logger.Log(Logger.LogLevel.Info, "Using a supplied formatter");

string[] inputs = { "()", ")((()))", "(", "(())((()())())" };

for(int i = 0; i < inputs.Length; i ++)
{
    ParenthesesValidator.IsValid(inputs[i]);
}

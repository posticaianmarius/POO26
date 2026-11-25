using System.Globalization;
using System.Text.RegularExpressions;

public class Toolbox
{
    private readonly Logger logger = new("Toolbox");

    // Calculator pattern
    private static readonly Regex Calculation = new(@"^\s*([+-]?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+))\s*([+*/-])\s*([+-]?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+))\s*$");

    // Execute tool
    public string Run(string toolName, string input)
    {
        string result = toolName switch
        {
            "get_time" => DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            "get_date" => DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "calculator" => Calculate(input),
            _ => "Error: unknown tool."
        };

        if(result.StartsWith("Error:")) logger.Log(Logger.LogLevel.Error, $"{toolName}: {result}");
        else logger.Log(Logger.LogLevel.Trace, $"{toolName} returned: {result}");

        return result;
    }

    // Evaluate expression
    private static string Calculate(string input)
    {
        Match match = Calculation.Match(input);

        // Parse operands
        if(!match.Success
        || !decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal left)
        || !decimal.TryParse(match.Groups[3].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal right))
        {
            return "Error: enter two numbers and one operator";
        }

        string operation = match.Groups[2].Value;

        if(operation == "/" && right == 0) return "Error: division by zero.";

        decimal result = operation switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" => left / right,
            _ => 0
        };

        return result.ToString();
    }
}
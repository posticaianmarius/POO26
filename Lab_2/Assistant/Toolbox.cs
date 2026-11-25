using System.Globalization;
using System.Text.RegularExpressions;

namespace Zeya.Assistant;

public sealed class Toolbox
{
    private static readonly Regex Calculation = new(@"^\s*([+-]?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+))\s*([+*/-])\s*([+-]?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+))\s*$");

    public string Run(string toolName, string input)
    {
        ArgumentNullException.ThrowIfNull(toolName);
        ArgumentNullException.ThrowIfNull(input);

        return toolName switch
        {
            "get_time" => DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            "get_date" => DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "calculator" => Calculate(input),
            _ => "Error: unknown tool. Use get_time, get_date, or calculator."
        };
    }

    private static string Calculate(string input)
    {
        Match match = Calculation.Match(input);
        if(!match.Success
            || !decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal left)
            || !decimal.TryParse(match.Groups[3].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal right))
        {
            return "Error: enter two numbers and one operator, such as 12 * 7.";
        }

        string operation = match.Groups[2].Value;
        if(operation == "/" && right == 0) return "Error: division by zero.";

        try
        {
            decimal result = operation switch
            {
                "+" => left + right,
                "-" => left - right,
                "*" => left * right,
                "/" => left / right,
                _ => throw new InvalidOperationException("The calculator matched an unsupported operator.")
            };
            return result.ToString("G29", CultureInfo.InvariantCulture);
        }
        catch(OverflowException)
        {
            return "Error: calculation result is out of range.";
        }
    }
}

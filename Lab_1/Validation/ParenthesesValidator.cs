class ParenthesesValidator
{
    private static readonly Logger logger = new Logger("Parentheses");

    // Validate parentheses
    public static bool IsValid(string input)
    {
        int balance = 0;

        // Check each character
        for(int i = 0; i < input.Length; i ++)
        {
            if(input[i] == '(')
            {
                balance ++;
                logger.Log(Logger.LogLevel.Trace, "Opening found");
            }
            else if(input[i] == ')')
            {
                // Check for extra closing
                if(balance == 0)
                {
                    logger.Log(Logger.LogLevel.Warn, "Unexpected closing");
                    logger.Log(Logger.LogLevel.Error, $"Invalid: {input} - extra closing");
                    logger.Log(Logger.LogLevel.Info, "Result: False");
                    return false;
                }

                balance --;
                logger.Log(Logger.LogLevel.Info, "Pair found");
            }
        }

        // Check for unmatched opening
        bool valid = balance == 0;

        if(!valid) logger.Log(Logger.LogLevel.Error, $"Invalid: {input} - missing closing");

        logger.Log(Logger.LogLevel.Info, $"Result: {valid}");
        return valid;
    }
}

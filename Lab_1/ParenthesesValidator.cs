class ParenthesesValidator
{
    private static readonly Logger logger = new Logger("Parentheses");

    public static bool IsValid(string input)
    {
        int balance = 0;

        for(int i = 0; i < input.Length; i ++)
        {
            if(input[i] == '(')
            {
                balance ++;
                logger.Log(Logger.LogLevel.Trace, "Opening found");
            }
            else if(input[i] == ')')
            {
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

        bool valid = balance == 0;

        if(!valid) logger.Log(Logger.LogLevel.Error, $"Invalid: {input} - missing closing");

        logger.Log(Logger.LogLevel.Info, $"Result: {valid}");
        return valid;
    }
}
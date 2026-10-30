
Logger.SetLevel(Logger.LogLevel.Error);
Logger.SetFormat("[%t] [%l] %n: %m");

string[] inputs = { "()", ")((()))", "(", "(())((()())())" };

for(int i = 0; i < inputs.Length; i ++)
{
    ParenthesesValidator.IsValid(inputs[i]);
}
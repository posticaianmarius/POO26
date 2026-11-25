class ConsoleSink
{
    public void Write(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Console.WriteLine(line);
    }
}

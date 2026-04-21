public static class Logger
{
    private static readonly string LogPath = "app.log";
    private static readonly object _lock = new object();

    public static void Log(string message)
    {
        string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        
        lock (_lock)
        {
            File.AppendAllText(LogPath, entry + Environment.NewLine);
        }
        
        Console.WriteLine(entry);
    }
}
namespace LangAngo.CSharp;

public enum LogLevel
{
    Debug,
    Verbose,
    Info,
    Warning,
    Error
}

public static class Logger
{
    private static LogLevel _minLevel = LogLevel.Info;
    private static bool _initialized;

    public static void Initialize(LogLevel minLevel = LogLevel.Info)
    {
        _minLevel = minLevel;
        _initialized = true;
    }

    public static void SetLevel(string? level)
    {
        if (string.IsNullOrEmpty(level)) return;
        
        _minLevel = level.ToLowerInvariant() switch
        {
            "debug" => LogLevel.Debug,
            "verbose" => LogLevel.Verbose,
            "info" => LogLevel.Info,
            "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            _ => LogLevel.Info
        };
    }

    public static bool IsEnabled(LogLevel level) => level >= _minLevel && _initialized;

    public static void Debug(string message) => Log(LogLevel.Debug, message);
    public static void Verbose(string message) => Log(LogLevel.Verbose, message);
    public static void Info(string message) => Log(LogLevel.Info, message);
    public static void Warning(string message) => Log(LogLevel.Warning, message);
    public static void Error(string message) => Log(LogLevel.Error, message);

    public static void Debug(string format, params object[] args) => Log(LogLevel.Debug, string.Format(format, args));
    public static void Verbose(string format, params object[] args) => Log(LogLevel.Verbose, string.Format(format, args));
    public static void Info(string format, params object[] args) => Log(LogLevel.Info, string.Format(format, args));
    public static void Warning(string format, params object[] args) => Log(LogLevel.Warning, string.Format(format, args));
    public static void Error(string format, params object[] args) => Log(LogLevel.Error, string.Format(format, args));

    private static void Log(LogLevel level, string message)
    {
        if (!IsEnabled(level)) return;
        
        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        var prefix = level switch
        {
            LogLevel.Debug => "[DBG]",
            LogLevel.Verbose => "[VRB]",
            LogLevel.Info => "[INF]",
            LogLevel.Warning => "[WRN]",
            LogLevel.Error => "[ERR]",
            _ => "[LOG]"
        };
        
        Console.WriteLine($"[{timestamp}] {prefix} [LangAngo] {message}");
    }
}

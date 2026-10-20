namespace Menhir.Editor.Logging;

/// <summary>
/// The editor's log: what the Console panel shows. Messages are also written to
/// stdout (info) or stderr (warnings and errors), so they show up in a terminal too.
/// </summary>
public static class Log
{
    public static EditorLog Messages { get; } = new();

    public static void Info(string message) => Write(LogLevel.Info, message, null);

    public static void Warning(string message) => Write(LogLevel.Warning, message, null);

    public static void Error(string message) => Write(LogLevel.Error, message, null);

    /// <summary>Logs an error; the exception's type and stack trace go in the details.</summary>
    public static void Error(string message, Exception exception) =>
        Write(LogLevel.Error, message, exception.ToString());

    private static void Write(LogLevel level, string message, string? details)
    {
        Messages.Add(level, message, details);

        TextWriter writer = level == LogLevel.Info ? Console.Out : Console.Error;
        string prefix = level switch
        {
            LogLevel.Warning => "Warning: ",
            LogLevel.Error => "Error: ",
            _ => "",
        };
        writer.WriteLine(prefix + message);
        if (details != null)
            writer.WriteLine(details);
    }
}

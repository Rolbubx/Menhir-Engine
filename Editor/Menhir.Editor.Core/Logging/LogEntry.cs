namespace Menhir.Editor.Logging;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <param name="Sequence">Increases with every message, so it identifies an entry even after older ones are dropped.</param>
/// <param name="Details">Extra text shown only when the entry is selected, e.g. an exception's stack trace.</param>
public sealed record LogEntry(long Sequence, DateTime Time, LogLevel Level, string Message, string? Details)
{
    /// <summary>First line of the message, for one-line lists.</summary>
    public string Summary
    {
        get
        {
            int newline = Message.IndexOfAny(['\r', '\n']);
            return newline < 0 ? Message : Message[..newline];
        }
    }
}

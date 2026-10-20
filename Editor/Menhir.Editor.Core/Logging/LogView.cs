using System.Text.Json.Serialization;

namespace Menhir.Editor.Logging;

/// <summary>What the Console panel shows: which levels, a text search, and whether repeats are collapsed.</summary>
public sealed class LogFilter
{
    public bool ShowInfo { get; set; } = true;
    public bool ShowWarnings { get; set; } = true;
    public bool ShowErrors { get; set; } = true;

    /// <summary>Show identical messages once, with how many times they were logged.</summary>
    public bool Collapse { get; set; }

    /// <summary>Case-insensitive text the message must contain; empty shows everything.</summary>
    [JsonIgnore]
    public string Search { get; set; } = "";

    public bool Shows(LogLevel level) => level switch
    {
        LogLevel.Info => ShowInfo,
        LogLevel.Warning => ShowWarnings,
        _ => ShowErrors,
    };
}

/// <param name="Entry">The message, or with Collapse, its latest occurrence.</param>
/// <param name="Count">How many times it was logged (1 unless collapsed).</param>
public readonly record struct LogRow(LogEntry Entry, int Count);

public readonly record struct LogCounts(int Info, int Warnings, int Errors);

public static class LogView
{
    /// <summary>
    /// The rows to show, oldest first. Collapsed messages stay at the position
    /// of their first occurrence.
    /// </summary>
    public static List<LogRow> Build(IReadOnlyList<LogEntry> entries, LogFilter filter)
    {
        var rows = new List<LogRow>();
        var collapsedRows = new Dictionary<(LogLevel, string, string?), int>();

        foreach (LogEntry entry in entries)
        {
            if (!filter.Shows(entry.Level))
                continue;
            if (filter.Search.Length > 0 && !entry.Message.Contains(filter.Search, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!filter.Collapse)
            {
                rows.Add(new LogRow(entry, 1));
                continue;
            }

            var key = (entry.Level, entry.Message, entry.Details);
            if (collapsedRows.TryGetValue(key, out int index))
            {
                rows[index] = new LogRow(entry, rows[index].Count + 1);
            }
            else
            {
                collapsedRows[key] = rows.Count;
                rows.Add(new LogRow(entry, 1));
            }
        }
        return rows;
    }

    /// <summary>Messages per level, ignoring the filter (for the level toggle buttons).</summary>
    public static LogCounts Count(IReadOnlyList<LogEntry> entries)
    {
        int info = 0, warnings = 0, errors = 0;
        foreach (LogEntry entry in entries)
        {
            switch (entry.Level)
            {
                case LogLevel.Info: info++; break;
                case LogLevel.Warning: warnings++; break;
                default: errors++; break;
            }
        }
        return new LogCounts(info, warnings, errors);
    }
}

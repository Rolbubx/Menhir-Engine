namespace Menhir.Editor.Logging;

/// <summary>
/// In-memory list of the most recent log messages. Messages can be added from any thread.
/// </summary>
public sealed class EditorLog
{
    private readonly object _lock = new();
    private readonly List<LogEntry> _entries = new();
    private long _nextSequence = 1;

    public EditorLog(int capacity = 5000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    /// <summary>Oldest messages are dropped once there are more than this.</summary>
    public int Capacity { get; }

    /// <summary>Changes whenever a message is added or the log is cleared, so views know when to rebuild.</summary>
    public int Version { get; private set; }

    public int Count
    {
        get
        {
            lock (_lock)
                return _entries.Count;
        }
    }

    /// <summary>The most recent message, or null when the log is empty.</summary>
    public LogEntry? Last
    {
        get
        {
            lock (_lock)
                return _entries.Count > 0 ? _entries[^1] : null;
        }
    }

    public LogEntry Add(LogLevel level, string message, string? details = null)
    {
        lock (_lock)
        {
            var entry = new LogEntry(_nextSequence++, DateTime.Now, level, message, details);
            _entries.Add(entry);
            if (_entries.Count > Capacity)
                _entries.RemoveRange(0, _entries.Count - Capacity);
            Version++;
            return entry;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            Version++;
        }
    }

    /// <summary>A copy of the messages, oldest first.</summary>
    public List<LogEntry> GetEntries()
    {
        lock (_lock)
            return new List<LogEntry>(_entries);
    }
}

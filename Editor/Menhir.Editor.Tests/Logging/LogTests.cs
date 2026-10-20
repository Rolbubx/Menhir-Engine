using Menhir.Editor.Logging;

namespace Menhir.Editor.Tests.Logging;

public class LogTests
{
    [Fact]
    public void Add_KeepsOrderAndIncreasesSequence()
    {
        var log = new EditorLog();
        LogEntry first = log.Add(LogLevel.Info, "a");
        LogEntry second = log.Add(LogLevel.Error, "b", "stack");

        List<LogEntry> entries = log.GetEntries();
        Assert.Equal(["a", "b"], entries.Select(e => e.Message));
        Assert.True(second.Sequence > first.Sequence);
        Assert.Equal("stack", entries[1].Details);
    }

    [Fact]
    public void Add_DropsOldestBeyondCapacity()
    {
        var log = new EditorLog(capacity: 3);
        for (int i = 0; i < 5; i++)
            log.Add(LogLevel.Info, i.ToString());

        Assert.Equal(["2", "3", "4"], log.GetEntries().Select(e => e.Message));
    }

    [Fact]
    public void Version_ChangesOnAddAndClear()
    {
        var log = new EditorLog();
        int v0 = log.Version;
        log.Add(LogLevel.Info, "a");
        int v1 = log.Version;
        log.Clear();

        Assert.NotEqual(v0, v1);
        Assert.NotEqual(v1, log.Version);
        Assert.Equal(0, log.Count);
    }

    [Fact]
    public void Add_IsSafeFromSeveralThreads()
    {
        var log = new EditorLog(capacity: 10_000);
        Parallel.For(0, 4, _ =>
        {
            for (int i = 0; i < 1000; i++)
                log.Add(LogLevel.Info, "x");
        });

        List<LogEntry> entries = log.GetEntries();
        Assert.Equal(4000, entries.Count);
        Assert.Equal(4000, entries.Select(e => e.Sequence).Distinct().Count());
    }

    [Fact]
    public void Summary_IsFirstLine()
    {
        var entry = new LogEntry(1, DateTime.Now, LogLevel.Info, "first\nsecond", null);
        Assert.Equal("first", entry.Summary);
    }

    private static List<LogEntry> Sample()
    {
        var log = new EditorLog();
        log.Add(LogLevel.Info, "Opened scene");
        log.Add(LogLevel.Warning, "Missing parent");
        log.Add(LogLevel.Error, "Could not save");
        log.Add(LogLevel.Warning, "Missing parent");
        log.Add(LogLevel.Info, "Saved scene");
        return log.GetEntries();
    }

    [Fact]
    public void Build_WithoutFilter_ShowsEverything()
    {
        List<LogRow> rows = LogView.Build(Sample(), new LogFilter());
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.Count));
    }

    [Fact]
    public void Build_HidesDisabledLevels()
    {
        var filter = new LogFilter { ShowInfo = false, ShowErrors = false };
        List<LogRow> rows = LogView.Build(Sample(), filter);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(LogLevel.Warning, r.Entry.Level));
    }

    [Fact]
    public void Build_SearchIsCaseInsensitive()
    {
        List<LogRow> rows = LogView.Build(Sample(), new LogFilter { Search = "SCENE" });
        Assert.Equal(["Opened scene", "Saved scene"], rows.Select(r => r.Entry.Message));
    }

    [Fact]
    public void Build_Collapse_GroupsRepeatsAtFirstPositionWithLatestEntry()
    {
        List<LogEntry> entries = Sample();
        List<LogRow> rows = LogView.Build(entries, new LogFilter { Collapse = true });

        Assert.Equal(["Opened scene", "Missing parent", "Could not save", "Saved scene"],
            rows.Select(r => r.Entry.Message));
        Assert.Equal(2, rows[1].Count);
        Assert.Same(entries[3], rows[1].Entry);
    }

    [Fact]
    public void Build_Collapse_KeepsDifferentDetailsApart()
    {
        var log = new EditorLog();
        log.Add(LogLevel.Error, "Failed", "stack A");
        log.Add(LogLevel.Error, "Failed", "stack B");

        Assert.Equal(2, LogView.Build(log.GetEntries(), new LogFilter { Collapse = true }).Count);
    }

    [Fact]
    public void Count_IgnoresFilter()
    {
        Assert.Equal(new LogCounts(2, 2, 1), LogView.Count(Sample()));
    }
}

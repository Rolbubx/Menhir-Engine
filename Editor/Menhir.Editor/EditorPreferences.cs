using System.Text.Json;
using Menhir.Editor.Logging;

namespace Menhir.Editor;

/// <summary>
/// Editor state that ImGui's ini file does not cover: main window size,
/// which panels are open and the recently opened projects.
/// </summary>
public sealed class EditorPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int WindowWidth { get; set; } = 1600;
    public int WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; }

    /// <summary><see cref="UI.EditorDockspace.LayoutVersion"/> of the saved layout.ini.</summary>
    public int LayoutVersion { get; set; }

    /// <summary>Panel title → is the panel open.</summary>
    public Dictionary<string, bool> OpenPanels { get; set; } = new();

    /// <summary>Project panel shows icons (true) or a list.</summary>
    public bool ProjectGridView { get; set; } = true;

    /// <summary>Console panel toggles (levels shown, collapse).</summary>
    public LogFilter ConsoleFilter { get; set; } = new();

    /// <summary>Root folders of recently opened projects, most recent first.</summary>
    public List<string> RecentProjects { get; set; } = new();

    private const int MaxRecentProjects = 10;

    public void AddRecentProject(string path)
    {
        RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, path);
        if (RecentProjects.Count > MaxRecentProjects)
            RecentProjects.RemoveRange(MaxRecentProjects, RecentProjects.Count - MaxRecentProjects);
    }

    public static EditorPreferences Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<EditorPreferences>(File.ReadAllText(path), JsonOptions) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warning($"Could not read editor preferences, using defaults: {e.Message}");
        }
        return new();
    }

    public void Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not save editor preferences: {e.Message}");
        }
    }
}

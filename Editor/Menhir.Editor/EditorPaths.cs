namespace Menhir.Editor;

/// <summary>
/// Per-user locations for editor state that is not part of any project
/// (window layout, preferences). Lives in the OS config dir, e.g.
/// %APPDATA%/Menhir/Editor on Windows or ~/.config/Menhir/Editor on Linux.
/// </summary>
public static class EditorPaths
{
    public static string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Menhir", "Editor");

    public static string LayoutFile => Path.Combine(ConfigDirectory, "layout.ini");

    public static string PreferencesFile => Path.Combine(ConfigDirectory, "preferences.json");

    public static void EnsureConfigDirectory() => Directory.CreateDirectory(ConfigDirectory);
}

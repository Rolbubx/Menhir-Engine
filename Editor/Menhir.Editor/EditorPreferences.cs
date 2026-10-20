using System.Text.Json;

namespace Menhir.Editor;

/// <summary>
/// Editor state that ImGui's ini file does not cover: main window size and
/// which panels are open.
/// </summary>
public sealed class EditorPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int WindowWidth { get; set; } = 1600;
    public int WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; }

    /// <summary>Panel title → is the panel open.</summary>
    public Dictionary<string, bool> OpenPanels { get; set; } = new();

    public static EditorPreferences Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<EditorPreferences>(File.ReadAllText(path), JsonOptions) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not read editor preferences, using defaults: {e.Message}");
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
            Console.Error.WriteLine($"Could not save editor preferences: {e.Message}");
        }
    }
}

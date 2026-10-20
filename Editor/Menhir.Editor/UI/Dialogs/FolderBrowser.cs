using System.Numerics;
using ImGuiNET;
using Menhir.Editor.Projects;

namespace Menhir.Editor.UI.Dialogs;

/// <summary>
/// Minimal file-system folder browser drawn with ImGui (no native dialog dependency):
/// a path field, an "Up" button and the subfolders of the current folder.
/// Menhir projects are marked in the list.
/// </summary>
public sealed class FolderBrowser
{
    private string _currentPath = "";
    private string _pathBuffer = "";
    private List<string> _folders = new();
    private string? _error;

    public string CurrentPath => _currentPath;

    /// <summary>Full path of the subfolder selected with a single click, or null.</summary>
    public string? SelectedFolder { get; private set; }

    public void Navigate(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            _folders = Directory.EnumerateDirectories(fullPath)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _currentPath = fullPath;
            _error = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                      or NotSupportedException)
        {
            _error = e.Message;
        }
        _pathBuffer = _currentPath;
        SelectedFolder = null;
    }

    /// <summary>Draws the browser. Returns a folder the user double-clicked, or null.</summary>
    public string? Draw(Vector2 size)
    {
        string? activated = null;

        if (ImGui.Button("Up") && Directory.GetParent(_currentPath) is { } parent)
            Navigate(parent.FullName);
        ImGui.SameLine();
        if (OperatingSystem.IsWindows())
        {
            DrawDriveCombo();
            ImGui.SameLine();
        }
        ImGui.SetNextItemWidth(-float.Epsilon);
        if (ImGui.InputText("##path", ref _pathBuffer, 1024, ImGuiInputTextFlags.EnterReturnsTrue))
            Navigate(_pathBuffer);

        if (ImGui.BeginChild("##folders", size, ImGuiChildFlags.Border))
        {
            foreach (string folder in _folders)
            {
                bool isProject = Project.IsProjectFolder(folder);
                string label = isProject ? $"{Path.GetFileName(folder)}   (Menhir project)" : Path.GetFileName(folder);
                if (ImGui.Selectable($"{label}##{folder}", folder == SelectedFolder, ImGuiSelectableFlags.AllowDoubleClick))
                {
                    SelectedFolder = folder;
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        activated = folder;
                }
            }
            if (_folders.Count == 0)
                ImGui.TextDisabled("No subfolders.");
        }
        ImGui.EndChild();
        ErrorTextLine(_error);

        return activated;
    }

    private void DrawDriveCombo()
    {
        string currentRoot = Path.GetPathRoot(_currentPath) ?? "";
        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
        if (!ImGui.BeginCombo("##drive", currentRoot))
            return;
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (ImGui.Selectable(drive.Name, drive.Name == currentRoot))
                Navigate(drive.RootDirectory.FullName);
        }
        ImGui.EndCombo();
    }

    private static void ErrorTextLine(string? error)
    {
        if (error != null)
            ImGui.TextColored(new Vector4(1f, 0.45f, 0.4f, 1f), error);
    }
}

using System.Numerics;
using ImGuiNET;
using Menhir.Editor.Projects;

namespace Menhir.Editor.UI.Dialogs;

/// <summary>Picks an existing project folder (one that contains Assets/).</summary>
public sealed class OpenProjectDialog : ModalDialog
{
    private readonly FolderBrowser _browser = new();
    private Action<Project>? _onOpened;
    private string? _error;

    protected override string Title => "Open Project";

    protected override Vector2? InitialSize => new Vector2(720, 460);

    public void Open(string startFolder, Action<Project> onOpened)
    {
        _browser.Navigate(startFolder);
        _onOpened = onOpened;
        _error = null;
        RequestOpen();
    }

    protected override void DrawContent()
    {
        float footer = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetTextLineHeightWithSpacing();
        string? activated = _browser.Draw(new Vector2(0, -footer));
        if (activated != null)
        {
            if (Project.IsProjectFolder(activated))
                TryOpen(activated);
            else
                _browser.Navigate(activated);
        }

        string target = _browser.SelectedFolder is { } selected && Project.IsProjectFolder(selected)
            ? selected
            : _browser.CurrentPath;
        bool valid = Project.IsProjectFolder(target);
        if (valid)
            ImGui.TextUnformatted(target);
        else
            ImGui.TextDisabled("Select a project folder (it contains an Assets folder).");
        ErrorText(_error);

        if (Button("Open", valid, primary: true))
            TryOpen(target);
        ImGui.SameLine();
        if (Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
            Close();
    }

    private void TryOpen(string path)
    {
        try
        {
            Project project = Project.Open(path);
            Close();
            _onOpened?.Invoke(project);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _error = e.Message;
        }
    }
}

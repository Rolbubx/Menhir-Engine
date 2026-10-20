using System.Numerics;
using ImGuiNET;

namespace Menhir.Editor.UI.Dialogs;

/// <summary>
/// "Browse..." for a folder path field: a modal <see cref="FolderBrowser"/> with Select / Cancel.
/// Call <see cref="Draw"/> every frame in the window that has the field.
/// </summary>
public sealed class FolderPickerPopup
{
    private const string PopupId = "Choose a Folder";

    private readonly FolderBrowser _browser = new();
    private Action<string>? _onPicked;
    private bool _openRequested;

    public void Open(string startFolder, Action<string> onPicked)
    {
        _browser.Navigate(Directory.Exists(startFolder)
            ? startFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        _onPicked = onPicked;
        _openRequested = true;
    }

    public void Draw()
    {
        if (_openRequested)
        {
            ImGui.OpenPopup(PopupId);
            _openRequested = false;
        }

        ImGui.SetNextWindowSize(new Vector2(640, 420), ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f));
        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.NoSavedSettings))
            return;

        float footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetTextLineHeightWithSpacing() + 4;
        string? activated = _browser.Draw(new Vector2(0, -footer));
        if (activated != null)
            _browser.Navigate(activated);

        string chosen = _browser.SelectedFolder ?? _browser.CurrentPath;
        ImGui.TextDisabled(chosen);
        if (EditorTheme.PrimaryButton("Select", new Vector2(ImGui.GetFontSize() * 5.5f, 0)))
        {
            _onPicked?.Invoke(chosen);
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(ImGui.GetFontSize() * 5.5f, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}

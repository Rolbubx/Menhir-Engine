using ImGuiNET;

namespace Menhir.Editor.UI.Dialogs;

/// <summary>"Save changes to X?" with Save / Don't Save / Cancel.</summary>
public sealed class UnsavedChangesDialog : ModalDialog
{
    private string _sceneName = "";
    private Action? _onSave;
    private Action? _onDiscard;

    protected override string Title => "Unsaved Changes";

    public void Open(string sceneName, Action onSave, Action onDiscard)
    {
        _sceneName = sceneName;
        _onSave = onSave;
        _onDiscard = onDiscard;
        RequestOpen();
    }

    protected override void DrawContent()
    {
        ImGui.TextUnformatted($"Save changes to the scene '{_sceneName}'?");
        ImGui.TextDisabled("Your changes will be lost if you don't save them.");
        ImGui.Spacing();

        if (ImGui.Button("Save"))
        {
            Close();
            _onSave?.Invoke();
        }
        ImGui.SameLine();
        if (ImGui.Button("Don't Save"))
        {
            Close();
            _onDiscard?.Invoke();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
            Close();
    }
}

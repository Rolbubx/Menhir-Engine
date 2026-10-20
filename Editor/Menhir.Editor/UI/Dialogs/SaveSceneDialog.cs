using ImGuiNET;
using Menhir.Editor.Assets;

namespace Menhir.Editor.UI.Dialogs;

/// <summary>"Save Scene As": a file name and a folder of the project's Assets.</summary>
public sealed class SaveSceneDialog : ModalDialog
{
    private readonly EditorContext _context;
    private Action? _onSaved;
    private string _name = "";
    private string _folder = AssetPaths.Root;
    private string? _error;
    private bool _focusName;

    public SaveSceneDialog(EditorContext context)
    {
        _context = context;
    }

    protected override string Title => "Save Scene As";

    public void Open(Action? onSaved = null)
    {
        AssetDatabase assets = _context.Project!.Assets;
        _folder = _context.ActiveScenePath is { } path ? AssetPaths.GetParent(path)! :
            assets.IsFolder("Assets/Scenes") ? "Assets/Scenes" : AssetPaths.Root;
        _name = _context.ActiveSceneGuid == null ? "New Scene" : _context.ActiveScene.Name;
        _onSaved = onSaved;
        _error = null;
        _focusName = true;
        RequestOpen();
    }

    protected override void DrawContent()
    {
        AssetDatabase assets = _context.Project!.Assets;

        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 24);
        if (ImGui.BeginCombo("Folder", _folder))
        {
            IEnumerable<string> folders = assets.GetAllEntries().Where(e => e.IsFolder).Select(e => e.Path)
                .Prepend(AssetPaths.Root).Order(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in folders)
            {
                if (ImGui.Selectable(folder, folder == _folder))
                    _folder = folder;
            }
            ImGui.EndCombo();
        }

        if (_focusName)
        {
            ImGui.SetKeyboardFocusHere();
            _focusName = false;
        }
        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 24);
        bool submitted = ImGui.InputText("Name", ref _name, 128,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        ImGui.TextDisabled($"{_folder}/{_name.Trim()}.scene");
        ErrorText(_error);
        ImGui.Spacing();

        if (ImGui.Button("Save") || submitted)
        {
            try
            {
                _context.SaveSceneAs(_folder, _name);
                Close();
                _onSaved?.Invoke();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _error = e.Message;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
            Close();
    }
}

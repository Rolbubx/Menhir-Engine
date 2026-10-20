using System.Numerics;
using ImGuiNET;
using Menhir.Editor.Projects;

namespace Menhir.Editor.UI.Dialogs;

/// <summary>Creates a project: a name and the folder to create it in.</summary>
public sealed class NewProjectDialog : ModalDialog
{
    private readonly FolderBrowser _browser = new();
    private Action<Project>? _onCreated;
    private string _name = "";
    private string? _error;
    private bool _focusName;

    protected override string Title => "New Project";

    protected override Vector2? InitialSize => new Vector2(720, 480);

    public void Open(string startFolder, Action<Project> onCreated)
    {
        _browser.Navigate(startFolder);
        _onCreated = onCreated;
        _name = "New Project";
        _error = null;
        _focusName = true;
        RequestOpen();
    }

    protected override void DrawContent()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Name");
        ImGui.SameLine();
        if (_focusName)
        {
            ImGui.SetKeyboardFocusHere();
            _focusName = false;
        }
        ImGui.SetNextItemWidth(-float.Epsilon);
        bool submitted = ImGui.InputText("##name", ref _name, 128,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

        ImGui.TextDisabled("Location");
        float footer = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetTextLineHeightWithSpacing();
        string? activated = _browser.Draw(new Vector2(0, -footer));
        if (activated != null)
            _browser.Navigate(activated);

        string location = _browser.SelectedFolder ?? _browser.CurrentPath;
        string name = _name.Trim();
        ImGui.TextDisabled(name.Length > 0 ? Path.Combine(location, name) : "Enter a name.");
        ErrorText(_error);

        if (Button("Create", name.Length > 0) || (submitted && name.Length > 0))
        {
            try
            {
                Project project = Project.Create(location, name);
                Close();
                _onCreated?.Invoke(project);
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

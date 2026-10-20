using System.Numerics;
using ImGuiNET;
using Menhir.Editor.Inspector;
using Menhir.Editor.Projects;
using Menhir.Editor.UI;

namespace Menhir.Editor.Panels;

/// <summary>
/// Edit > Project Settings: the open project's settings, saved in ProjectSettings/ProjectSettings.json.
/// For now the rendering pipeline's (shadows), like URP's pipeline asset. Edits are not undoable.
/// </summary>
public sealed class ProjectSettingsPanel : EditorPanel
{
    private readonly EditorContext _context;
    private bool _dirty;

    public ProjectSettingsPanel(EditorContext context)
    {
        _context = context;
    }

    public override string Title => "Project Settings";

    public override string Icon => Icons.Settings;

    public override bool OpenByDefault => false;

    protected override Vector2? DefaultSize => new Vector2(460, 560);

    protected override void DrawContent()
    {
        if (_context.Project is not { } project)
        {
            Placeholder("No project open.");
            return;
        }

        ProjectSettings settings = project.Settings;
        ImGui.PushFont(EditorFonts.Bold);
        ImGui.TextUnformatted(project.Name);
        ImGui.PopFont();
        ImGui.TextDisabled($"{ProjectTemplates.Find(settings.Type)?.DisplayName ?? settings.Type}  ·  {project.RootPath}");
        if (settings.Remote is { } remote)
            ImGui.TextDisabled($"Remote: {remote.Url} (not available yet)");

        if (ComponentHeader.Draw("rendering", Icons.Light, EditorTheme.Text, "Rendering: Shadows", menuId: "##renderingMenu"))
        {
            ComponentDrawer.Draw(settings.Rendering, (property, value) =>
            {
                property.SetValue(settings.Rendering, value);
                _dirty = true;
            }, project.Assets);
        }
        if (ImGui.BeginPopup("##renderingMenu"))
        {
            if (ImGui.MenuItem("Reset to Defaults"))
            {
                settings.Rendering = new RenderingSettings();
                _dirty = true;
            }
            ImGui.EndPopup();
        }

        // Saved once a drag or text edit is finished, not on every frame of it.
        if (_dirty && !ImGui.IsAnyItemActive())
        {
            try
            {
                project.SaveSettings();
                _dirty = false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Logging.Log.Error($"Could not save the project settings: {e.Message}");
                _dirty = false;
            }
        }
    }
}

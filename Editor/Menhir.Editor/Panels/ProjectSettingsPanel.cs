using System.Numerics;
using ImGuiNET;
using Menhir.Editor.Input;
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

        if (ComponentHeader.Draw("input", Icons.Input, EditorTheme.Text, "Input", menuId: "##inputMenu"))
            DrawInputSettings(project);
        if (ImGui.BeginPopup("##inputMenu"))
        {
            if (ImGui.MenuItem("Reset to Defaults"))
            {
                settings.Input = new InputSettings();
                _dirty = true;
            }
            ImGui.EndPopup();
        }

        if (ComponentHeader.Draw("build", Icons.Settings, EditorTheme.Text, "Build", menuId: "##buildMenu"))
            DrawBuildSettings(project);
        if (ImGui.BeginPopup("##buildMenu"))
        {
            if (ImGui.MenuItem("Reset to Defaults"))
            {
                settings.Build = new BuildSettings();
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

    /// <summary>The Input Manager: one collapsible entry per axis or button, like Unity's.</summary>
    private void DrawInputSettings(Project project)
    {
        List<InputAxis> axes = project.Settings.Input.Axes;
        ImGui.TextDisabled("Read by name in the game (Fly Camera, Scene Switcher, and scripts later).");
        for (int i = 0; i < axes.Count; i++)
        {
            InputAxis axis = axes[i];
            ImGui.PushID(i);
            var warnings = new List<string>();
            _ = new InputState([axis], warnings);
            string label = warnings.Count > 0 ? $"{Icons.Warning}  {axis.Name}" : axis.Name;
            bool open = ImGui.TreeNodeEx($"{label}###axis", ImGuiTreeNodeFlags.SpanAvailWidth);
            if (ImGui.BeginPopupContextItem("##axisMenu"))
            {
                if (ImGui.MenuItem("Duplicate"))
                {
                    axes.Insert(i + 1, axis.Clone());
                    _dirty = true;
                }
                if (ImGui.MenuItem("Remove"))
                {
                    axes.RemoveAt(i);
                    _dirty = true;
                }
                ImGui.EndPopup();
            }
            if (open)
            {
                foreach (string warning in warnings)
                    ImGui.TextColored(EditorTheme.Warning, warning);
                ComponentDrawer.Draw(axis, (property, value) =>
                {
                    property.SetValue(axis, value);
                    _dirty = true;
                }, project.Assets);
                ImGui.TreePop();
            }
            ImGui.PopID();
            if (i >= axes.Count || axes[i] != axis)
                break; // removed or duplicated: the list changed under the loop
        }
        if (ImGui.Button($"{Icons.Plus}  Add Axis"))
        {
            axes.Add(new InputAxis());
            _dirty = true;
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Right-click an entry to duplicate or remove it.");
    }

    /// <summary>The window size and compiler, then the scenes in the build (all of them when none is ticked).</summary>
    private void DrawBuildSettings(Project project)
    {
        BuildSettings build = project.Settings.Build;
        ComponentDrawer.Draw(build, (property, value) =>
        {
            property.SetValue(build, value);
            _dirty = true;
        }, project.Assets);

        ImGui.Spacing();
        ImGui.TextDisabled(build.Scenes.Count == 0
            ? "Scenes in the build: all (the open one first). Tick some to choose; the first ticked one starts the game."
            : "Scenes in the build, in this order (the first starts the game):");
        foreach (string path in _context.FindSceneAssets().Select(a => a.Path))
        {
            bool included = build.Scenes.Contains(path);
            if (ImGui.Checkbox(path, ref included))
            {
                if (included)
                    build.Scenes.Add(path);
                else
                    build.Scenes.Remove(path);
                _dirty = true;
            }
        }
        foreach (string missing in build.Scenes.Where(p => _context.FindSceneAssets().All(a => a.Path != p)).ToList())
        {
            ImGui.TextColored(EditorTheme.Warning, $"Missing: {missing}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Remove##{missing}"))
            {
                build.Scenes.Remove(missing);
                _dirty = true;
            }
        }
        ImGui.TextDisabled($"Output: {Path.Combine(project.BuildDirectory, Build.GameBuild.SafeName(project.Name))}");
    }
}

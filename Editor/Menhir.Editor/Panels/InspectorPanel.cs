using System.Numerics;
using System.Reflection;
using ImGuiNET;
using Menhir.Editor.Assets;
using Menhir.Editor.Commands;
using Menhir.Editor.Inspector;
using Menhir.Editor.Inspection;
using Menhir.Editor.Mathematics;
using Menhir.Editor.PostProcessing;
using Menhir.Editor.Scenes;
using Menhir.Editor.Scripting;
using Menhir.Editor.UI;

namespace Menhir.Editor.Panels;

/// <summary>
/// Properties of the selection. For an entity: name, Transform, then each component drawn
/// by reflection (<see cref="ComponentDrawer"/>). For an asset: its path and .meta content.
/// </summary>
public sealed class InspectorPanel : EditorPanel
{
    private readonly EditorContext _context;
    private readonly ScriptManager _scripts;

    // Scene changes from menus are applied after the components are drawn, so the list is not
    // modified while it is being iterated.
    private Action? _pendingChange;

    private static readonly PropertyInfo ActiveProperty =
        typeof(PostProcessEffect).GetProperty(nameof(PostProcessEffect.Active))!;

    private string _componentSearch = "";
    private string _nameBuffer = "";
    private long _editingNameId;

    // Euler angles are cached while the rotation is unchanged, so values the user typed
    // (e.g. 180° yaw, or pitch past 90°) are not replaced by an equivalent set every frame.
    private long _eulerEntityId;
    private Quaternion _eulerSource;
    private Vector3 _euler;

    public InspectorPanel(EditorContext context, ScriptManager scripts)
    {
        _context = context;
        _scripts = scripts;
    }

    public override string Title => "Inspector";

    public override string Icon => Icons.Inspector;

    protected override void DrawContent()
    {
        if (_context.SelectedEntity is { } entity)
            DrawEntity(entity);
        else if (_context.SelectedAsset is { } asset)
            DrawAsset(asset);
        else
            Placeholder("Nothing selected.");

        _pendingChange?.Invoke();
        _pendingChange = null;
    }

    private void DrawEntity(Entity entity)
    {
        ImGui.PushID($"entity{entity.LocalId}");

        DrawName(entity);

        if (ComponentHeader.Draw("transform", Icons.Transform, EditorTheme.Text, "Transform", menuId: null))
            DrawTransform(entity);

        for (int i = 0; i < entity.Components.Count; i++)
        {
            Component component = entity.Components[i];
            ImGui.PushID(i);
            if (component is MissingComponent missing)
            {
                if (ComponentHeader.Draw("component", Icons.MissingComponent, EditorTheme.Warning,
                        $"{missing.TypeName} (Missing)", "##componentMenu"))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Warning);
                    ImGui.TextWrapped($"The component type '{missing.TypeName}' was not found. " +
                                      "Its data is kept and saved back unchanged.");
                    ImGui.PopStyleColor();
                }
            }
            else if (component is ScriptComponent script)
            {
                Type? scriptType = _scripts.Current?.Find(script.ClassName);
                bool notFound = scriptType == null && !_scripts.IsCompiling;
                if (ComponentHeader.Draw("component", Icons.Script, notFound ? EditorTheme.Warning : EditorTheme.Text,
                        ScriptDrawer.Title(script) + (notFound ? " (Missing)" : " (Script)"), "##componentMenu"))
                    ScriptDrawer.Draw(script, scriptType, _scripts.IsCompiling, _context.Execute);
            }
            else
            {
                Type type = component.GetType();
                bool? active = (component as PostProcessEffect)?.Active;
                bool open = ComponentHeader.Draw("component", ComponentDrawer.IconFor(type), EditorTheme.Text,
                    InspectableProperties.NicifyName(type.Name), "##componentMenu", active, out bool toggled);
                if (toggled && component is PostProcessEffect effect)
                    _context.Execute(new SetPropertyCommand(effect, ActiveProperty, !effect.Active));
                if (open)
                {
                    ImGui.BeginDisabled(active == false);
                    ComponentDrawer.Draw(component, _context.Execute, _context.Project?.Assets);
                    ImGui.EndDisabled();
                }
            }
            DrawComponentMenu(entity, component);
            ImGui.PopID();
        }

        DrawAddComponent(entity);
        ImGui.PopID();
    }

    private void DrawComponentMenu(Entity entity, Component component)
    {
        if (!ImGui.BeginPopup("##componentMenu"))
            return;
        if (ImGui.MenuItem($"{Icons.Trash}  Remove Component"))
            _pendingChange = () => _context.Execute(new RemoveComponentCommand(entity, component));
        ImGui.EndPopup();
    }

    /// <summary>The wide button under the components, with a searchable list of component types.</summary>
    private void DrawAddComponent(Entity entity)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        float width = MathF.Min(ImGui.GetContentRegionAvail().X, ImGui.GetFontSize() * 16);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X - width) / 2);
        Vector2 buttonPos = ImGui.GetCursorScreenPos();
        if (ImGui.Button("Add Component", new Vector2(width, ImGui.GetFrameHeight() + 4)))
        {
            _componentSearch = "";
            ImGui.OpenPopup("##addComponent");
        }

        ImGui.SetNextWindowPos(buttonPos + new Vector2(0, ImGui.GetFrameHeight() + 6));
        ImGui.SetNextWindowSize(new Vector2(width, 0));
        if (!ImGui.BeginPopup("##addComponent"))
            return;

        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(-float.Epsilon);
        ImGui.InputTextWithHint("##search", $"{Icons.Search}  Search", ref _componentSearch, 64);
        ImGui.Separator();

        // Grouped by category (Rendering, Post-processing, then the rest), like Unity's menu.
        var groups = ComponentTypes.All
            .Where(t => t != typeof(ScriptComponent))
            .Where(t => InspectableProperties.NicifyName(t.Name).Contains(_componentSearch.Trim(), StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t.GetCustomAttribute<ComponentCategoryAttribute>()?.Name ?? "Other")
            .OrderBy(g => g.Key switch { "Rendering" => 0, "Post-processing" => 1, _ => 2 })
            .ThenBy(g => g.Key, StringComparer.Ordinal);
        bool any = false;
        foreach (var group in groups)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.TextDim);
            ImGui.SeparatorText(group.Key);
            ImGui.PopStyleColor();
            any = true;
            foreach (Type type in group.OrderBy(t => t.Name, StringComparer.Ordinal))
                DrawAddComponentItem(entity, type);
        }
        any |= DrawAddScriptItems(entity);
        if (!any)
            ImGui.TextDisabled("No component matches.");
        ImGui.EndPopup();
    }

    private void DrawAddComponentItem(Entity entity, Type type)
    {
        // One of each kind per entity, for now.
        bool present = entity.Components.Any(c => c.GetType() == type);
        if (ImGui.Selectable($"{ComponentDrawer.IconFor(type)}  {InspectableProperties.NicifyName(type.Name)}", false,
                present ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None))
        {
            Component component = ComponentTypes.Create(ComponentTypes.GetName(type))!;
            _pendingChange = () => _context.Execute(new AddComponentCommand(entity, component));
        }
    }

    /// <summary>The project's script classes (Project > Create > C# Script makes one).</summary>
    private bool DrawAddScriptItems(Entity entity)
    {
        string search = _componentSearch.Trim();
        List<Type> types = (_scripts.Current?.ScriptTypes ?? [])
            .Where(t => InspectableProperties.NicifyName(t.Name).Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        t.FullName!.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (types.Count == 0 && (search.Length > 0 || !_scripts.IsCompiling))
            return false;

        ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.TextDim);
        ImGui.SeparatorText("Scripts");
        ImGui.PopStyleColor();
        if (types.Count == 0)
            ImGui.TextDisabled("Compiling the scripts...");
        foreach (Type type in types)
        {
            string className = type.FullName!;
            bool present = entity.Components.OfType<ScriptComponent>().Any(c => c.ClassName == className);
            string label = InspectableProperties.NicifyName(type.Name) + (type.Namespace != null ? $"  ({type.Namespace})" : "");
            if (ImGui.Selectable($"{Icons.Script}  {label}", false, present ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None))
            {
                var component = new ScriptComponent { ClassName = className };
                _pendingChange = () => _context.Execute(new AddComponentCommand(entity, component));
            }
        }
        return true;
    }

    private static void DrawAsset(AssetEntry asset)
    {
        // Header: big tinted icon, name and kind.
        Vector2 start = ImGui.GetCursorPos();
        EditorFonts.LargeIcon(AssetIcons.Icon(asset), AssetIcons.Color(asset));
        Vector2 end = ImGui.GetCursorPos();
        float textX = start.X + EditorFonts.LargeIconSize + 10;
        ImGui.SetCursorPos(new Vector2(textX, start.Y + 4));
        ImGui.PushFont(EditorFonts.Bold);
        ImGui.TextUnformatted(asset.Name);
        ImGui.PopFont();
        ImGui.SetCursorPosX(textX);
        ImGui.TextDisabled(asset.IsFolder ? "Folder" : $"{asset.Meta.Importer} asset");
        ImGui.SetCursorPos(new Vector2(start.X, MathF.Max(end.Y, ImGui.GetCursorPosY())));

        if (ComponentHeader.Draw("asset", Icons.Info, EditorTheme.Text, "Asset", menuId: null) && PropertyTable.Begin("##asset"))
        {
            PropertyTable.Row("Path");
            ReadOnlyField("##path", asset.Path);
            PropertyTable.Row("GUID");
            ReadOnlyField("##guid", asset.Guid.ToString("N"));
            PropertyTable.Row("Importer");
            ImGui.TextUnformatted(asset.Meta.Importer);
            PropertyTable.End();
        }

        if (!asset.IsFolder && ComponentHeader.Draw("import", Icons.Component, EditorTheme.Text, "Import Settings", menuId: null))
        {
            if (asset.Meta.Settings.Count > 0)
                ImGui.TextUnformatted(asset.Meta.Settings.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Placeholder("Import settings become editable once asset importing exists.");
        }
    }

    /// <summary>Text the user can select and copy, but not edit.</summary>
    private static void ReadOnlyField(string id, string text)
    {
        ImGui.PushFont(EditorFonts.Mono);
        ImGui.InputText(id, ref text, (uint)text.Length + 1, ImGuiInputTextFlags.ReadOnly);
        ImGui.PopFont();
    }

    /// <summary>Edited in a buffer and applied when the field loses focus, so an empty name never shows up.</summary>
    private void DrawName(Entity entity)
    {
        if (_editingNameId != entity.LocalId)
            _nameBuffer = entity.Name;

        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(Icons.Entity);
        ImGui.SameLine();
        string id = $"#{entity.LocalId}";
        ImGui.SetNextItemWidth(-(ImGui.CalcTextSize(id).X + ImGui.GetStyle().ItemSpacing.X));
        ImGui.InputText("##name", ref _nameBuffer, 256);
        _editingNameId = ImGui.IsItemActive() ? entity.LocalId : 0;
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            string name = _nameBuffer.Trim();
            if (name.Length > 0 && name != entity.Name)
                _context.Execute(new RenameEntityCommand(entity, name));
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(id);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Local ID of the entity in its scene");
    }

    private void DrawTransform(Entity entity)
    {
        if (!PropertyTable.Begin("##transform"))
            return;

        Transform transform = entity.Transform;

        Vector3 position = transform.Position;
        PropertyTable.Row("Position");
        if (PropertyTable.Vector("##position", ref position, 0.05f))
            SetTransform(entity, TransformState.From(transform) with { Position = position });

        if (entity.LocalId != _eulerEntityId || transform.Rotation != _eulerSource)
        {
            _eulerEntityId = entity.LocalId;
            _eulerSource = transform.Rotation;
            _euler = EulerAngles.FromQuaternion(transform.Rotation);
        }
        Vector3 euler = _euler;
        PropertyTable.Row("Rotation");
        if (PropertyTable.Vector("##rotation", ref euler, 0.5f, "%.2f°"))
        {
            SetTransform(entity, TransformState.From(transform) with { Rotation = EulerAngles.ToQuaternion(euler) });
            _eulerSource = transform.Rotation;
            _euler = euler;
        }

        Vector3 scale = transform.Scale;
        PropertyTable.Row("Scale");
        if (PropertyTable.Vector("##scale", ref scale, 0.01f))
            SetTransform(entity, TransformState.From(transform) with { Scale = scale });

        PropertyTable.End();
    }

    /// <summary>Each frame of a drag is a command; the history merges them into one undo step.</summary>
    private void SetTransform(Entity entity, TransformState state) =>
        _context.Execute(new SetTransformCommand(entity, state));
}

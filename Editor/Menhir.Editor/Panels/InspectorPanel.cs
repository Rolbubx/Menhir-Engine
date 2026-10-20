using System.Numerics;
using ImGuiNET;
using Menhir.Editor.Assets;
using Menhir.Editor.Inspector;
using Menhir.Editor.Inspection;
using Menhir.Editor.Mathematics;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Panels;

/// <summary>
/// Properties of the selection. For an entity: name, Transform, then each component drawn
/// by reflection (<see cref="ComponentDrawer"/>). For an asset: its path and .meta content.
/// </summary>
public sealed class InspectorPanel : EditorPanel
{
    private readonly EditorContext _context;

    private string _nameBuffer = "";
    private long _editingNameId;

    // Euler angles are cached while the rotation is unchanged, so values the user typed
    // (e.g. 180° yaw, or pitch past 90°) are not replaced by an equivalent set every frame.
    private long _eulerEntityId;
    private Quaternion _eulerSource;
    private Vector3 _euler;

    public InspectorPanel(EditorContext context)
    {
        _context = context;
    }

    public override string Title => "Inspector";

    protected override void DrawContent()
    {
        if (_context.SelectedEntity is { } entity)
            DrawEntity(entity);
        else if (_context.SelectedAsset is { } asset)
            DrawAsset(asset);
        else
            Placeholder("Nothing selected.");
    }

    private void DrawEntity(Entity entity)
    {
        ImGui.PushID($"entity{entity.LocalId}");

        DrawName(entity);
        ImGui.Spacing();

        if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
            DrawTransform(entity);

        for (int i = 0; i < entity.Components.Count; i++)
        {
            Component component = entity.Components[i];
            string title = InspectableProperties.NicifyName(component.GetType().Name);
            ImGui.PushID(i);
            if (component is MissingComponent missing)
            {
                if (ImGui.CollapsingHeader($"Missing: {missing.TypeName}", ImGuiTreeNodeFlags.DefaultOpen))
                    Placeholder($"The component type '{missing.TypeName}' was not found. Its data is kept and saved back unchanged.");
            }
            else if (ImGui.CollapsingHeader(title, ImGuiTreeNodeFlags.DefaultOpen) && ComponentDrawer.Draw(component))
            {
                _context.MarkSceneDirty();
            }
            ImGui.PopID();
        }

        ImGui.PopID();
    }

    private static void DrawAsset(AssetEntry asset)
    {
        ImGui.TextUnformatted(asset.Name);
        ImGui.SameLine();
        ImGui.TextDisabled(asset.IsFolder ? "Folder" : asset.Meta.Importer);
        ImGui.Spacing();

        if (ImGui.CollapsingHeader("Asset", ImGuiTreeNodeFlags.DefaultOpen) && PropertyTable.Begin("##asset"))
        {
            PropertyTable.Row("Path");
            ReadOnlyField("##path", asset.Path);
            PropertyTable.Row("GUID");
            ReadOnlyField("##guid", asset.Guid.ToString("N"));
            PropertyTable.Row("Importer");
            ImGui.TextUnformatted(asset.Meta.Importer);
            PropertyTable.End();
        }

        if (!asset.IsFolder && ImGui.CollapsingHeader("Import Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (asset.Meta.Settings.Count > 0)
                ImGui.TextUnformatted(asset.Meta.Settings.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Placeholder("Import settings become editable once asset importing exists.");
        }
    }

    /// <summary>Text the user can select and copy, but not edit.</summary>
    private static void ReadOnlyField(string id, string text)
    {
        ImGui.InputText(id, ref text, (uint)text.Length + 1, ImGuiInputTextFlags.ReadOnly);
    }

    /// <summary>Edited in a buffer and applied when the field loses focus, so an empty name never shows up.</summary>
    private void DrawName(Entity entity)
    {
        if (_editingNameId != entity.LocalId)
            _nameBuffer = entity.Name;

        string id = $"#{entity.LocalId}";
        ImGui.SetNextItemWidth(-(ImGui.CalcTextSize(id).X + ImGui.GetStyle().ItemSpacing.X));
        ImGui.InputText("##name", ref _nameBuffer, 256);
        _editingNameId = ImGui.IsItemActive() ? entity.LocalId : 0;
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            string name = _nameBuffer.Trim();
            if (name.Length > 0 && name != entity.Name)
            {
                entity.Name = name;
                _context.MarkSceneDirty();
            }
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
        if (ImGui.DragFloat3("##position", ref position, 0.05f, 0, 0, "%.3f"))
        {
            transform.Position = position;
            _context.MarkSceneDirty();
        }

        if (entity.LocalId != _eulerEntityId || transform.Rotation != _eulerSource)
        {
            _eulerEntityId = entity.LocalId;
            _eulerSource = transform.Rotation;
            _euler = EulerAngles.FromQuaternion(transform.Rotation);
        }
        Vector3 euler = _euler;
        PropertyTable.Row("Rotation");
        if (ImGui.DragFloat3("##rotation", ref euler, 0.5f, 0, 0, "%.2f°"))
        {
            transform.Rotation = EulerAngles.ToQuaternion(euler);
            _eulerSource = transform.Rotation;
            _euler = euler;
            _context.MarkSceneDirty();
        }

        Vector3 scale = transform.Scale;
        PropertyTable.Row("Scale");
        if (ImGui.DragFloat3("##scale", ref scale, 0.01f, 0, 0, "%.3f"))
        {
            transform.Scale = scale;
            _context.MarkSceneDirty();
        }

        PropertyTable.End();
    }
}

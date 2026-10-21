using System.Numerics;
using System.Reflection;
using ImGuiNET;
using Menhir.Editor.Assets;
using Menhir.Editor.Commands;
using Menhir.Editor.Inspection;
using Menhir.Editor.Mathematics;
using Menhir.Editor.PostProcessing;
using Menhir.Editor.Scenes;
using Menhir.Editor.UI;

namespace Menhir.Editor.Inspector;

/// <summary>
/// Draws any component by reflection: every property found by
/// <see cref="InspectableProperties"/> gets a widget picked from its type and attributes.
/// Edits are applied as <see cref="SetPropertyCommand"/>s so they can be undone.
/// </summary>
public static class ComponentDrawer
{
    /// <summary>Icon shown in the component's header, the Add Component menu and the hierarchy.</summary>
    public static string IconFor(Type type)
    {
        if (type == typeof(MeshRenderer))
            return Icons.MeshRenderer;
        if (type == typeof(Light))
            return Icons.Light;
        if (type == typeof(Camera))
            return Icons.Camera;
        if (type == typeof(ProceduralSkybox))
            return Icons.Sky;
        if (type.IsSubclassOf(typeof(PostProcessEffect)))
            return Icons.PostProcess;
        return Icons.Component;
    }

    /// <param name="assets">The project's assets, for texture fields (null: none to pick from).</param>
    public static void Draw(Component component, Action<ICommand> execute, AssetDatabase? assets) =>
        Draw(component, (property, value) => execute(new SetPropertyCommand(component, property, value)), assets);

    /// <summary>Draws the properties of any object; <paramref name="set"/> applies an edit.</summary>
    public static void Draw(object target, Action<PropertyInfo, object?> set, AssetDatabase? assets)
    {
        if (!PropertyTable.Begin("##properties"))
            return;

        foreach (PropertyInfo property in InspectableProperties.Get(target.GetType()))
        {
            PropertyTable.Row(InspectableProperties.NicifyName(property.Name),
                property.GetCustomAttribute<TooltipAttribute>()?.Text);
            ImGui.PushID(property.Name);
            object? value = property.GetValue(target);
            if (DrawValue(property, value, assets, out object? newValue))
                set(property, newValue);
            ImGui.PopID();
        }

        PropertyTable.End();
    }

    /// <summary>Draws the widget for one value. Returns true with the new value when it was edited.</summary>
    private static bool DrawValue(PropertyInfo property, object? value, AssetDatabase? assets, out object? newValue)
    {
        newValue = value;
        var range = property.GetCustomAttribute<RangeAttribute>();
        float min = property.GetCustomAttribute<MinAttribute>()?.Value ?? float.MinValue;

        switch (value)
        {
            case bool b:
                if (!ImGui.Checkbox("##value", ref b))
                    return false;
                newValue = b;
                return true;

            case int i:
                bool intChanged = range != null
                    ? ImGui.SliderInt("##value", ref i, (int)range.Min, (int)range.Max, "%d", ImGuiSliderFlags.AlwaysClamp)
                    : ImGui.DragInt("##value", ref i, 0.1f, min > int.MinValue ? (int)min : int.MinValue, int.MaxValue);
                if (!intChanged)
                    return false;
                newValue = i;
                return true;

            case float f:
                bool floatChanged = range != null
                    ? ImGui.SliderFloat("##value", ref f, range.Min, range.Max, "%.3f", ImGuiSliderFlags.AlwaysClamp)
                    : ImGui.DragFloat("##value", ref f, DragSpeed(f), min, float.MaxValue, "%.3f",
                        min > float.MinValue ? ImGuiSliderFlags.AlwaysClamp : ImGuiSliderFlags.None);
                if (!floatChanged)
                    return false;
                newValue = f;
                return true;

            case string s:
                if (!ImGui.InputText("##value", ref s, 1024))
                    return false;
                newValue = s;
                return true;

            case Vector2 v2:
                if (!PropertyTable.Vector("##value", ref v2, 0.01f))
                    return false;
                newValue = v2;
                return true;

            case Vector3 v3:
                if (!PropertyTable.Vector("##value", ref v3, 0.01f))
                    return false;
                newValue = v3;
                return true;

            case Vector4 v4 when property.GetCustomAttribute<ColorAttribute>() != null:
                Vector4 shown = ColorSpace.LinearToGamma(v4);
                if (!ColorField(ref shown, alpha: true))
                    return false;
                newValue = ColorSpace.GammaToLinear(shown);
                return true;

            case Vector4 v4 when property.GetCustomAttribute<TrackballAttribute>() != null:
                return TrackballField(v4, out newValue);

            case Vector4 v4:
                if (!PropertyTable.Vector("##value", ref v4, 0.01f))
                    return false;
                newValue = v4;
                return true;

            case Enum e:
                return DrawEnum(e, out newValue);

            case ColorCurve curve:
                if (!CurveEditor.Draw("##curve", curve, CurveColor(property.Name), out ColorCurve edited))
                    return false;
                newValue = edited;
                return true;

            case Guid or null when property.PropertyType == typeof(Guid?) && property.GetCustomAttribute<TextureAssetAttribute>() != null:
                return AssetField((Guid?)value, assets, AssetImporters.Texture, Icons.Texture, out newValue);

            case Guid or null when property.PropertyType == typeof(Guid?) && property.GetCustomAttribute<ModelAssetAttribute>() != null:
                return AssetField((Guid?)value, assets, AssetImporters.Model, Icons.Model, out newValue);

            default:
                ImGui.TextDisabled($"({property.PropertyType.Name} not supported yet)");
                return false;
        }
    }

    private static float DragSpeed(float value) => MathF.Max(0.01f, MathF.Abs(value) * 0.01f);

    private static Vector4 CurveColor(string name) => name switch
    {
        "Red" => new Vector4(0.95f, 0.35f, 0.35f, 1f),
        "Green" => new Vector4(0.4f, 0.85f, 0.4f, 1f),
        "Blue" => new Vector4(0.4f, 0.6f, 1f, 1f),
        _ => new Vector4(0.9f, 0.9f, 0.9f, 1f),
    };

    /// <summary>A color bar filling the field, like Unity's; clicking it opens a picker (HDR values allowed).</summary>
    private static bool ColorField(ref Vector4 color, bool alpha)
    {
        ImGuiColorEditFlags flags = ImGuiColorEditFlags.HDR | ImGuiColorEditFlags.Float |
                                    (alpha ? ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.AlphaBar : ImGuiColorEditFlags.NoAlpha);
        Vector4 preview = alpha ? color : color with { W = 1f };
        if (ImGui.ColorButton("##swatch", preview, flags, new Vector2(ImGui.CalcItemWidth(), ImGui.GetFrameHeight())))
            ImGui.OpenPopup("##picker");

        bool changed = false;
        if (ImGui.BeginPopup("##picker"))
        {
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 14);
            changed = ImGui.ColorPicker4("##picker", ref color, flags);
            ImGui.EndPopup();
        }
        return changed;
    }

    /// <summary>Color wheel stand-in: the tint (as a color) and the offset (as a slider), on one line.</summary>
    private static bool TrackballField(Vector4 value, out object? newValue)
    {
        newValue = value;
        float width = ImGui.CalcItemWidth();
        float sliderWidth = MathF.Max(40f, width * 0.45f);
        float spacing = ImGui.GetStyle().ItemSpacing.X;

        Vector4 color = ColorSpace.LinearToGamma(value with { W = 1f });
        ImGui.SetNextItemWidth(width - sliderWidth - spacing);
        bool changed = ColorField(ref color, alpha: false);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Tint (its brightness is ignored)");
        ImGui.SameLine(0, spacing);
        float offset = value.W;
        ImGui.SetNextItemWidth(sliderWidth);
        if (ImGui.SliderFloat("##offset", ref offset, -1f, 1f, "%.2f"))
            changed = true;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Offset of every channel");

        if (!changed)
            return false;
        Vector4 linear = ColorSpace.GammaToLinear(color);
        newValue = new Vector4(linear.X, linear.Y, linear.Z, offset);
        return true;
    }

    /// <summary>Picks an asset of the project handled by <paramref name="importer"/>, or None.</summary>
    private static bool AssetField(Guid? value, AssetDatabase? assets, string importer, string icon, out object? newValue)
    {
        newValue = value;
        AssetEntry? current = value is { } guid ? assets?.FindByGuid(guid) : null;
        string label = value == null ? "None" : current != null ? $"{icon}  {current.Name}" : "Missing";
        bool changed = false;
        if (ImGui.BeginCombo("##asset", label))
        {
            if (ImGui.Selectable("None", value == null))
            {
                newValue = null;
                changed = value != null;
            }
            IEnumerable<AssetEntry> entries = (assets?.GetAllEntries() ?? [])
                .Where(e => !e.IsFolder && e.Meta.Importer == importer)
                .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase);
            foreach (AssetEntry entry in entries)
            {
                bool selected = entry.Guid == value;
                if (ImGui.Selectable($"{icon}  {entry.Name}##{entry.Guid}", selected) && !selected)
                {
                    newValue = (Guid?)entry.Guid;
                    changed = true;
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(entry.Path);
            }
            ImGui.EndCombo();
        }
        if (current != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(current.Path);
        return changed;
    }

    private static bool DrawEnum(Enum value, out object? newValue)
    {
        newValue = value;
        bool changed = false;
        if (ImGui.BeginCombo("##value", InspectableProperties.NicifyName(value.ToString())))
        {
            foreach (Enum option in Enum.GetValues(value.GetType()))
            {
                bool selected = option.Equals(value);
                if (ImGui.Selectable(InspectableProperties.NicifyName(option.ToString()), selected) && !selected)
                {
                    newValue = option;
                    changed = true;
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        return changed;
    }
}

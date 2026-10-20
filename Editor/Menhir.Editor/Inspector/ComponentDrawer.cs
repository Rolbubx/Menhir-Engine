using System.Numerics;
using System.Reflection;
using ImGuiNET;
using Menhir.Editor.Commands;
using Menhir.Editor.Inspection;
using Menhir.Editor.Mathematics;
using Menhir.Editor.Scenes;
using Menhir.Editor.UI;

namespace Menhir.Editor.Inspector;

/// <summary>
/// Draws any component by reflection: every property found by
/// <see cref="InspectableProperties"/> gets a widget picked from its type.
/// Edits are applied as <see cref="SetPropertyCommand"/>s so they can be undone.
/// </summary>
public static class ComponentDrawer
{
    /// <summary>Icon shown in the component's header and in the Add Component menu.</summary>
    public static string IconFor(Type type) => type == typeof(MeshRenderer) ? Icons.MeshRenderer : Icons.Component;

    public static void Draw(Component component, Action<ICommand> execute)
    {
        if (!PropertyTable.Begin("##properties"))
            return;

        foreach (PropertyInfo property in InspectableProperties.Get(component.GetType()))
        {
            PropertyTable.Row(InspectableProperties.NicifyName(property.Name));
            ImGui.PushID(property.Name);
            object? value = property.GetValue(component);
            if (DrawValue(property, value, out object? newValue))
                execute(new SetPropertyCommand(component, property, newValue));
            ImGui.PopID();
        }

        PropertyTable.End();
    }

    /// <summary>Draws the widget for one value. Returns true with the new value when it was edited.</summary>
    private static bool DrawValue(PropertyInfo property, object? value, out object? newValue)
    {
        newValue = value;
        switch (value)
        {
            case bool b:
                if (!ImGui.Checkbox("##value", ref b))
                    return false;
                newValue = b;
                return true;

            case int i:
                if (!ImGui.DragInt("##value", ref i))
                    return false;
                newValue = i;
                return true;

            case float f:
                if (!ImGui.DragFloat("##value", ref f, 0.01f))
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
                if (!ColorField(ref shown))
                    return false;
                newValue = ColorSpace.GammaToLinear(shown);
                return true;

            case Vector4 v4:
                if (!PropertyTable.Vector("##value", ref v4, 0.01f))
                    return false;
                newValue = v4;
                return true;

            case Enum e:
                return DrawEnum(e, out newValue);

            default:
                ImGui.TextDisabled($"({property.PropertyType.Name} not supported yet)");
                return false;
        }
    }

    /// <summary>A color bar filling the field, like Unity's; clicking it opens a picker.</summary>
    private static bool ColorField(ref Vector4 color)
    {
        const ImGuiColorEditFlags flags = ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.AlphaBar;
        if (ImGui.ColorButton("##swatch", color, flags, new Vector2(ImGui.CalcItemWidth(), ImGui.GetFrameHeight())))
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

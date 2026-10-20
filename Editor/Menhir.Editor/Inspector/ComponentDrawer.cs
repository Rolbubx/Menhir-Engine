using System.Numerics;
using System.Reflection;
using ImGuiNET;
using Menhir.Editor.Inspection;
using Menhir.Editor.Mathematics;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Inspector;

/// <summary>
/// Draws any component by reflection: every property found by
/// <see cref="InspectableProperties"/> gets a widget picked from its type.
/// </summary>
public static class ComponentDrawer
{
    /// <summary>Draws the component's properties. Returns true when one was edited.</summary>
    public static bool Draw(Component component)
    {
        if (!PropertyTable.Begin("##properties"))
            return false;

        bool changed = false;
        foreach (PropertyInfo property in InspectableProperties.Get(component.GetType()))
        {
            PropertyTable.Row(InspectableProperties.NicifyName(property.Name));
            ImGui.PushID(property.Name);
            object? value = property.GetValue(component);
            if (DrawValue(property, value, out object? newValue))
            {
                SetValue(component, property, newValue);
                changed = true;
            }
            ImGui.PopID();
        }

        PropertyTable.End();
        return changed;
    }

    // Single place where inspector edits reach a component (undo commands go here in Phase 7).
    private static void SetValue(Component component, PropertyInfo property, object? value) =>
        property.SetValue(component, value);

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
                if (!ImGui.DragFloat2("##value", ref v2, 0.01f))
                    return false;
                newValue = v2;
                return true;

            case Vector3 v3:
                if (!ImGui.DragFloat3("##value", ref v3, 0.01f))
                    return false;
                newValue = v3;
                return true;

            case Vector4 v4 when property.GetCustomAttribute<ColorAttribute>() != null:
                Vector4 shown = ColorSpace.LinearToGamma(v4);
                if (!ImGui.ColorEdit4("##value", ref shown))
                    return false;
                newValue = ColorSpace.GammaToLinear(shown);
                return true;

            case Vector4 v4:
                if (!ImGui.DragFloat4("##value", ref v4, 0.01f))
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

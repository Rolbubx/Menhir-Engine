using System.Numerics;
using System.Reflection;
using System.Text.Json;
using ImGuiNET;
using Menhir.Editor.Commands;
using Menhir.Editor.Inspection;
using Menhir.Editor.Mathematics;
using Menhir.Editor.Scenes;
using Menhir.Editor.Scripting;
using Menhir.Editor.UI;

namespace Menhir.Editor.Inspector;

/// <summary>
/// Draws a <see cref="ScriptComponent"/>: the fields its script declares (MH_FIELDS), with the scene's
/// saved values or the declared defaults. An edit replaces the component's field values in one
/// undoable command.
/// </summary>
public static class ScriptDrawer
{
    private static readonly PropertyInfo FieldsProperty = typeof(ScriptComponent).GetProperty(nameof(ScriptComponent.Fields))!;

    /// <summary>"Player Controller" for PlayerController.</summary>
    public static string Title(ScriptComponent component) =>
        InspectableProperties.NicifyName(component.ScriptName.Length > 0 ? component.ScriptName : "Script");

    /// <param name="type">The component's script, or null when the compiled scripts do not have it.</param>
    /// <param name="compiling">The scripts are being compiled (a missing script may be on its way).</param>
    public static void Draw(ScriptComponent component, ScriptType? type, bool compiling, Action<ICommand> execute)
    {
        if (type == null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, compiling ? EditorTheme.TextDim : EditorTheme.Warning);
            ImGui.TextWrapped(compiling
                ? "Compiling the scripts..."
                : $"The script '{component.ScriptName}' was not found: check the Console for compile errors, " +
                  "or that a .c file declares it with MH_SCRIPT. Its saved values are kept.");
            ImGui.PopStyleColor();
            return;
        }

        if (!PropertyTable.Begin("##fields"))
            return;
        PropertyTable.Row("Script");
        ImGui.TextDisabled(component.ScriptName);

        foreach (ScriptField field in type.Fields)
        {
            object value = ScriptFields.Value(field, component.Fields);
            PropertyTable.Row(InspectableProperties.NicifyName(field.Name), field.Tooltip);
            ImGui.PushID(field.Name);
            if (DrawValue(field, value, out object edited))
            {
                var fields = new Dictionary<string, JsonElement>(component.Fields)
                {
                    [field.Name] = ScriptFields.ToJson(field, edited),
                };
                execute(new SetPropertyCommand(component, FieldsProperty, fields));
            }
            ImGui.PopID();
        }
        PropertyTable.End();
    }

    private static bool DrawValue(ScriptField field, object value, out object edited)
    {
        edited = value;
        bool range = field.Min != null && field.Max != null;
        float min = field.Min ?? 0, max = field.Max ?? 0;
        switch (field.Kind)
        {
            case ScriptFieldKind.Bool:
            {
                bool b = value is true;
                if (!ImGui.Checkbox("##value", ref b))
                    return false;
                edited = b;
                return true;
            }
            case ScriptFieldKind.Int:
            {
                int i = value is int v ? v : 0;
                bool changed = range
                    ? ImGui.SliderInt("##value", ref i, (int)min, (int)max, "%d", ImGuiSliderFlags.AlwaysClamp)
                    : ImGui.DragInt("##value", ref i, 0.1f);
                edited = i;
                return changed;
            }
            case ScriptFieldKind.Float:
            {
                float f = value is float v ? v : 0;
                bool changed = range
                    ? ImGui.SliderFloat("##value", ref f, min, max, "%.3f", ImGuiSliderFlags.AlwaysClamp)
                    : ImGui.DragFloat("##value", ref f, MathF.Max(MathF.Abs(f) * 0.01f, 0.01f), float.MinValue, float.MaxValue, "%.3f");
                edited = f;
                return changed;
            }
            case ScriptFieldKind.String:
            {
                string s = value as string ?? "";
                if (!ImGui.InputText("##value", ref s, 4096))
                    return false;
                edited = s;
                return true;
            }
            case ScriptFieldKind.Enum:
                return DrawEnum(field, value is int index ? index : 0, out edited);
            case ScriptFieldKind.Vector2:
            {
                var v = value is Vector2 x ? x : default;
                if (!PropertyTable.Vector("##value", ref v, 0.01f))
                    return false;
                edited = v;
                return true;
            }
            case ScriptFieldKind.Vector3:
            {
                var v = value is Vector3 x ? x : default;
                if (!PropertyTable.Vector("##value", ref v, 0.01f))
                    return false;
                edited = v;
                return true;
            }
            case ScriptFieldKind.Color:
            {
                // Linear in the script, shown in gamma like the other colors.
                Vector4 shown = ColorSpace.LinearToGamma(value is Vector4 x ? x : Vector4.One);
                ImGui.SetNextItemWidth(-float.Epsilon);
                if (!ImGui.ColorEdit4("##value", ref shown, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.AlphaBar))
                    return false;
                edited = ColorSpace.GammaToLinear(shown);
                return true;
            }
            default:
                return false;
        }
    }

    private static bool DrawEnum(ScriptField field, int index, out object edited)
    {
        edited = index;
        bool changed = false;
        string current = index >= 0 && index < field.Options.Count ? InspectableProperties.NicifyName(field.Options[index]) : index.ToString();
        if (!ImGui.BeginCombo("##value", current))
            return false;
        for (int i = 0; i < field.Options.Count; i++)
        {
            bool selected = i == index;
            if (ImGui.Selectable(InspectableProperties.NicifyName(field.Options[i]), selected) && !selected)
            {
                edited = i;
                changed = true;
            }
            if (selected)
                ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
        return changed;
    }
}

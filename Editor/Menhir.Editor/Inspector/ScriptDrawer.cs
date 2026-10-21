using System.Reflection;
using System.Text.Json;
using ImGuiNET;
using Menhir.Editor.Commands;
using Menhir.Editor.Inspection;
using Menhir.Editor.Scenes;
using Menhir.Editor.Scripting;
using Menhir.Editor.UI;

namespace Menhir.Editor.Inspector;

/// <summary>
/// Draws a <see cref="ScriptComponent"/>: the public fields of its class, with the scene's saved values
/// (or the class's defaults). An edit replaces the component's field values in one undoable command.
/// </summary>
public static class ScriptDrawer
{
    private static readonly PropertyInfo FieldsProperty = typeof(ScriptComponent).GetProperty(nameof(ScriptComponent.Fields))!;

    // One instance of each class, for the values its fields start with. Emptied when the scripts are reloaded.
    private static readonly Dictionary<Type, object?> Defaults = new();

    public static void ClearCache() => Defaults.Clear();

    /// <summary>"Player Controller" for Game.PlayerController.</summary>
    public static string Title(ScriptComponent component)
    {
        string name = component.ClassName;
        int dot = name.LastIndexOf('.');
        return InspectableProperties.NicifyName(dot >= 0 ? name[(dot + 1)..] : name.Length > 0 ? name : "Script");
    }

    /// <param name="type">The component's class, or null when it is not found in the compiled scripts.</param>
    /// <param name="compiling">The scripts are being compiled (a missing class may be on its way).</param>
    public static void Draw(ScriptComponent component, Type? type, bool compiling, Action<ICommand> execute)
    {
        if (type == null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, compiling ? EditorTheme.TextDim : EditorTheme.Warning);
            ImGui.TextWrapped(compiling
                ? $"Compiling the scripts..."
                : $"The script class '{component.ClassName}' was not found: check the Console for compile errors, " +
                  "or that the class derives from Script. Its saved values are kept.");
            ImGui.PopStyleColor();
            return;
        }

        if (!Defaults.TryGetValue(type, out object? defaults))
        {
            try
            {
                defaults = Activator.CreateInstance(type);
            }
            catch (TargetInvocationException e)
            {
                Logging.Log.Error($"Creating {type.FullName} failed: {e.InnerException?.Message}");
                defaults = null;
            }
            Defaults[type] = defaults;
        }

        if (!PropertyTable.Begin("##fields"))
            return;
        PropertyTable.Row("Script");
        ImGui.TextDisabled(component.ClassName);

        foreach (ScriptField field in ScriptFields.Of(type))
        {
            object? value = defaults != null ? field.Field.GetValue(defaults) : null;
            if (component.Fields.TryGetValue(field.Name, out JsonElement saved) && ScriptFields.FromJson(field, saved) is { } loaded)
                value = loaded;

            PropertyTable.Row(InspectableProperties.NicifyName(field.Name), field.Tooltip);
            ImGui.PushID(field.Name);
            if (DrawValue(field, value, out object? edited))
            {
                var fields = new Dictionary<string, JsonElement>(component.Fields)
                {
                    [field.Name] = ScriptFields.ToJson(field.Kind, edited),
                };
                execute(new SetPropertyCommand(component, FieldsProperty, fields));
            }
            ImGui.PopID();
        }
        PropertyTable.End();
    }

    private static unsafe bool DrawValue(ScriptField field, object? value, out object? edited)
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
            case ScriptFieldKind.Long:
            {
                long l = value is long v ? v : 0;
                if (!ImGui.DragScalar("##value", ImGuiDataType.S64, (IntPtr)(&l), 0.1f))
                    return false;
                edited = l;
                return true;
            }
            case ScriptFieldKind.Float:
            {
                float f = value is float v ? v : 0;
                bool changed = range
                    ? ImGui.SliderFloat("##value", ref f, min, max, "%.3f", ImGuiSliderFlags.AlwaysClamp)
                    : ImGui.DragFloat("##value", ref f, DragSpeed(f), float.MinValue, float.MaxValue, "%.3f");
                edited = f;
                return changed;
            }
            case ScriptFieldKind.Double:
            {
                double d = value is double v ? v : 0;
                if (!ImGui.DragScalar("##value", ImGuiDataType.Double, (IntPtr)(&d), DragSpeed((float)d), IntPtr.Zero, IntPtr.Zero, "%.6g"))
                    return false;
                edited = d;
                return true;
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
                return DrawEnum(field.Field.FieldType, value, out edited);
            case ScriptFieldKind.Vector2:
            {
                var v = value is MenhirEngine.Vector2 m ? new System.Numerics.Vector2(m.x, m.y) : default;
                if (!PropertyTable.Vector("##value", ref v, 0.01f))
                    return false;
                edited = new MenhirEngine.Vector2(v.X, v.Y);
                return true;
            }
            case ScriptFieldKind.Vector3:
            {
                var v = value is MenhirEngine.Vector3 m ? new System.Numerics.Vector3(m.x, m.y, m.z) : default;
                if (!PropertyTable.Vector("##value", ref v, 0.01f))
                    return false;
                edited = new MenhirEngine.Vector3(v.X, v.Y, v.Z);
                return true;
            }
            default:
                return false;
        }
    }

    private static bool DrawEnum(Type type, object? value, out object? edited)
    {
        edited = value;
        bool changed = false;
        string current = value != null ? InspectableProperties.NicifyName(value.ToString()!) : "";
        if (!ImGui.BeginCombo("##value", current))
            return false;
        foreach (object option in Enum.GetValues(type))
        {
            bool selected = option.Equals(value);
            if (ImGui.Selectable(InspectableProperties.NicifyName(option.ToString()!), selected) && !selected)
            {
                edited = option;
                changed = true;
            }
            if (selected)
                ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
        return changed;
    }

    // Finer steps for small values, like the other drag fields.
    private static float DragSpeed(float value) => MathF.Max(MathF.Abs(value) * 0.01f, 0.01f);
}

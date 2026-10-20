using System.Numerics;
using ImGuiNET;
using Menhir.Editor.UI;

namespace Menhir.Editor.Inspector;

/// <summary>Two-column "label | widget" layout used by the inspector.</summary>
public static class PropertyTable
{
    private static readonly string[] AxisLabels = ["X", "Y", "Z", "W"];

    public static bool Begin(string id)
    {
        // Labels get a bit less than half of the width, like Unity's EditorGUIUtility.labelWidth.
        float labelWidth = MathF.Max(ImGui.GetContentRegionAvail().X * 0.45f - 40f, ImGui.GetFontSize() * 7);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 2));
        bool open = ImGui.BeginTable(id, 2, ImGuiTableFlags.None);
        ImGui.PopStyleVar();
        if (!open)
            return false;
        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, labelWidth);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        return true;
    }

    public static void End() => ImGui.EndTable();

    /// <summary>Starts a row with <paramref name="label"/>; the next widget fills the value column.</summary>
    public static void Row(string label)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(-float.Epsilon);
    }

    public static bool Vector(string id, ref Vector2 value, float speed, string format = "%.3f")
    {
        Span<float> v = [value.X, value.Y];
        if (!Vector(id, v, speed, format))
            return false;
        value = new Vector2(v[0], v[1]);
        return true;
    }

    public static bool Vector(string id, ref Vector3 value, float speed, string format = "%.3f")
    {
        Span<float> v = [value.X, value.Y, value.Z];
        if (!Vector(id, v, speed, format))
            return false;
        value = new Vector3(v[0], v[1], v[2]);
        return true;
    }

    public static bool Vector(string id, ref Vector4 value, float speed, string format = "%.3f")
    {
        Span<float> v = [value.X, value.Y, value.Z, value.W];
        if (!Vector(id, v, speed, format))
            return false;
        value = new Vector4(v[0], v[1], v[2], v[3]);
        return true;
    }

    /// <summary>"X [  ] Y [  ] Z [  ]": one drag field per axis sharing the available width, like Unity.</summary>
    private static bool Vector(string id, Span<float> values, float speed, string format)
    {
        float total = ImGui.CalcItemWidth();
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float inner = ImGui.GetStyle().ItemInnerSpacing.X;
        float labelWidth = ImGui.CalcTextSize("X").X;
        float fieldWidth = MathF.Max(1f, (total - values.Length * (labelWidth + inner) - (values.Length - 1) * spacing) / values.Length);

        bool changed = false;
        ImGui.PushID(id);
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, spacing);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(EditorTheme.TextDim, AxisLabels[i]);
            ImGui.SameLine(0, inner);
            ImGui.SetNextItemWidth(fieldWidth);
            changed |= ImGui.DragFloat($"##{i}", ref values[i], speed, 0, 0, format);
        }
        ImGui.PopID();
        return changed;
    }
}

using ImGuiNET;

namespace Menhir.Editor.Inspector;

/// <summary>Two-column "label | widget" layout used by the inspector.</summary>
public static class PropertyTable
{
    public static bool Begin(string id)
    {
        if (!ImGui.BeginTable(id, 2, ImGuiTableFlags.None))
            return false;
        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 7);
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
}

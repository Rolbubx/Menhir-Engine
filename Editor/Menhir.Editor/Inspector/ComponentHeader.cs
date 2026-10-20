using System.Numerics;
using ImGuiNET;
using Menhir.Editor.UI;

namespace Menhir.Editor.Inspector;

/// <summary>
/// Unity-style foldout bar above each component: icon, bold title and a "⋮" button at the
/// right. The open state is kept by ImGui per <c>id</c>.
/// </summary>
public static class ComponentHeader
{
    /// <summary>
    /// Returns true when the component is unfolded. <paramref name="menuId"/> is opened as a popup
    /// when the ⋮ button is clicked or the bar is right-clicked (pass null for no menu).
    /// With <paramref name="enabled"/>, a checkbox before the title turns the component on or off
    /// (<paramref name="toggled"/> tells when it was clicked).
    /// </summary>
    public static bool Draw(string id, string icon, Vector4 iconColor, string title, string? menuId) =>
        Draw(id, icon, iconColor, title, menuId, null, out _);

    public static bool Draw(string id, string icon, Vector4 iconColor, string title, string? menuId,
        bool? enabled, out bool toggled)
    {
        toggled = false;
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Header, EditorTheme.HeaderBar);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, EditorTheme.HeaderBarHover);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, EditorTheme.HeaderBarHover);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 5));
        ImGui.PushFont(EditorFonts.Bold);

        // The title is drawn by hand after the icon, so the icon can have its own color;
        // the checkbox, when there is one, goes between the arrow and the icon.
        float iconOffset = ImGui.GetTreeNodeToLabelSpacing();
        float checkboxWidth = enabled != null ? ImGui.GetTextLineHeight() + 6 : 0;
        string padding = enabled != null ? "           " : "     ";
        bool open = ImGui.CollapsingHeader($"{padding}{title}###{id}", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);
        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        bool rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);

        float textY = min.Y + (max.Y - min.Y - ImGui.GetTextLineHeight()) / 2;
        Vector4 shownIconColor = enabled == false ? iconColor with { W = 0.4f } : iconColor;
        ImGui.GetWindowDrawList().AddText(new Vector2(min.X + iconOffset + checkboxWidth, textY),
            ImGui.GetColorU32(shownIconColor), icon);

        ImGui.PopFont();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        if (enabled is { } on)
        {
            Vector2 after = ImGui.GetCursorScreenPos();
            float boxSize = ImGui.GetTextLineHeight();
            ImGui.SetCursorScreenPos(new Vector2(min.X + iconOffset - 2, min.Y + (max.Y - min.Y - boxSize) / 2 - 1));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(1, 1));
            if (ImGui.Checkbox($"##{id}enabled", ref on))
                toggled = true;
            ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(on ? "Active (uncheck to turn the effect off)" : "Turned off");
            ImGui.SetCursorScreenPos(after);
        }

        if (menuId != null)
        {
            float buttonWidth = ImGui.GetFrameHeight();
            ImGui.SameLine(ImGui.GetContentRegionMax().X - buttonWidth);
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, min.Y + (max.Y - min.Y - ImGui.GetFrameHeight()) / 2));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, ImGui.GetStyle().FramePadding.Y));
            bool clicked = EditorTheme.FlatButton($"{Icons.More}##{id}menu", new Vector2(buttonWidth, 0));
            ImGui.PopStyleVar();
            if (clicked || rightClicked)
                ImGui.OpenPopup(menuId);
        }
        return open;
    }
}

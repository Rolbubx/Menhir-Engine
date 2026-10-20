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
    /// </summary>
    public static bool Draw(string id, string icon, Vector4 iconColor, string title, string? menuId)
    {
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Header, EditorTheme.HeaderBar);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, EditorTheme.HeaderBarHover);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, EditorTheme.HeaderBarHover);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 5));
        ImGui.PushFont(EditorFonts.Bold);

        // The title is drawn by hand after the icon, so the icon can have its own color.
        float iconOffset = ImGui.GetTreeNodeToLabelSpacing();
        bool open = ImGui.CollapsingHeader($"     {title}###{id}", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);
        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        bool rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);

        float textY = min.Y + (max.Y - min.Y - ImGui.GetTextLineHeight()) / 2;
        ImGui.GetWindowDrawList().AddText(new Vector2(min.X + iconOffset, textY), ImGui.GetColorU32(iconColor), icon);

        ImGui.PopFont();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

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

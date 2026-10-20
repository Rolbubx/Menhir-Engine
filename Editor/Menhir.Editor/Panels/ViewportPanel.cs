using System.Numerics;
using ImGuiNET;

namespace Menhir.Editor.Panels;

public sealed class ViewportPanel : EditorPanel
{
    public override string Title => "Viewport";

    protected override ImGuiWindowFlags WindowFlags =>
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    // The scene texture will fill the whole window, edge to edge.
    protected override void PushWindowStyle() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    protected override void PopWindowStyle() => ImGui.PopStyleVar();

    protected override void DrawContent()
    {
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(8, 8));
        Placeholder("Scene rendering — Phase 2.");
    }
}

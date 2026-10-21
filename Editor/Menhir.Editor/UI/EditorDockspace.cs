using System.Numerics;
using ImGuiNET;

namespace Menhir.Editor.UI;

/// <summary>
/// Full-window dockspace hosting every editor panel. Builds the default layout
/// (hierarchy left, inspector right, project + console bottom, viewport and game center)
/// on first run or when a reset is requested; otherwise ImGui restores the
/// layout saved in layout.ini.
/// </summary>
public sealed class EditorDockspace
{
    private const string HostWindowName = "MenhirDockspaceHost";

    /// <summary>
    /// Bump when panel window IDs change, so a layout.ini saved by an older editor
    /// (which would leave the panels floating) is replaced by the default layout.
    /// </summary>
    public const int LayoutVersion = 3;

    private bool _resetRequested;

    public void RequestLayoutReset() => _resetRequested = true;

    /// <summary>
    /// Call once per frame, after the main menu bar and before drawing panels.
    /// <paramref name="bottomMargin"/> is left free at the bottom (for the status bar).
    /// </summary>
    public void Draw(float bottomMargin)
    {
        // Host window covering the work area (the main viewport minus the menu bar).
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        Vector2 size = viewport.WorkSize - new Vector2(0, bottomMargin);
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(size);
        ImGui.SetNextWindowViewport(viewport.ID);

        const ImGuiWindowFlags hostFlags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus |
            ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoBackground;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin(HostWindowName, hostFlags);
        ImGui.PopStyleVar(3);

        uint dockspaceId = ImGui.GetID("MenhirDockspace");
        if (_resetRequested)
        {
            BuildDefaultLayout(dockspaceId, size);
            _resetRequested = false;
        }

        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.None);
        ImGui.End();
    }

    private static void BuildDefaultLayout(uint dockspaceId, Vector2 size)
    {
        DockBuilder.ResetDockspace(dockspaceId, size);

        uint left = DockBuilder.Split(dockspaceId, ImGuiDir.Left, 0.18f, out uint rest);
        uint right = DockBuilder.Split(rest, ImGuiDir.Right, 0.24f, out rest);
        uint bottom = DockBuilder.Split(rest, ImGuiDir.Down, 0.28f, out uint center);

        // Window IDs, see EditorPanel.WindowId.
        DockBuilder.DockWindow("###Hierarchy", left);
        DockBuilder.DockWindow("###Inspector", right);
        DockBuilder.DockWindow("###Project", bottom);
        DockBuilder.DockWindow("###Console", bottom);
        DockBuilder.DockWindow("###Game", center);
        DockBuilder.DockWindow("###Viewport", center);
        DockBuilder.Finish(dockspaceId);
    }
}

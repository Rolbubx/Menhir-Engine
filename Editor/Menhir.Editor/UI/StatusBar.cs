using System.Numerics;
using ImGuiNET;
using Menhir.Editor.Logging;

namespace Menhir.Editor.UI;

/// <summary>
/// Strip at the bottom of the main window showing the latest log message, like Unity's.
/// Clicking it opens the Console.
/// </summary>
public sealed class StatusBar
{
    private readonly EditorLog _log;
    private readonly Action _showConsole;

    public StatusBar(EditorLog log, Action showConsole)
    {
        _log = log;
        _showConsole = showConsole;
    }

    public static float Height => ImGui.GetFrameHeight() + 2f;

    public void Draw()
    {
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos + new Vector2(0, viewport.WorkSize.Y - Height));
        ImGui.SetNextWindowSize(new Vector2(viewport.WorkSize.X, Height));
        ImGui.SetNextWindowViewport(viewport.ID);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus |
            ImGuiWindowFlags.NoFocusOnAppearing;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 1));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, EditorTheme.Background);
        bool visible = ImGui.Begin("##StatusBar", flags);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);

        if (visible && _log.Last is { } last)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(LogStyle.Color(last.Level), LogStyle.Icon(last.Level));
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, last.Level == LogLevel.Info ? EditorTheme.TextDim : EditorTheme.Text);
            ImGui.TextUnformatted(last.Summary);
            ImGui.PopStyleColor();

            // The whole bar is clickable.
            ImGui.SetCursorPos(Vector2.Zero);
            if (ImGui.InvisibleButton("##openConsole", ImGui.GetWindowSize()))
                _showConsole();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Open the Console");
        }
        ImGui.End();
    }
}

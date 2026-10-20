using ImGuiNET;

namespace Menhir.Editor.UI;

/// <summary>Helpers for editor-wide keyboard shortcuts.</summary>
public static class EditorInput
{
    /// <summary>Ctrl, or Cmd on macOS.</summary>
    public static bool Command
    {
        get
        {
            ImGuiIOPtr io = ImGui.GetIO();
            return io.KeyCtrl || (OperatingSystem.IsMacOS() && io.KeySuper);
        }
    }

    /// <summary>True on the frame <paramref name="key"/> is pressed with Ctrl (and Shift when asked).</summary>
    public static bool Shortcut(ImGuiKey key, bool shift = false) =>
        Command && ImGui.GetIO().KeyShift == shift && ImGui.IsKeyPressed(key, false);
}

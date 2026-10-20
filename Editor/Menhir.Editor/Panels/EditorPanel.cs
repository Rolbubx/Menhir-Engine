using System.Numerics;
using ImGuiNET;

namespace Menhir.Editor.Panels;

/// <summary>
/// A dockable editor window. The title is also the ImGui window ID, which is
/// what the dock layout (and layout.ini) refers to, so keep it stable.
/// </summary>
public abstract class EditorPanel
{
    public abstract string Title { get; }

    /// <summary>Shown before the title in the tab (one of <see cref="UI.Icons"/>).</summary>
    public abstract string Icon { get; }

    /// <summary>ImGui window ID: only the part after ### counts, so the icon is not part of it.</summary>
    public string WindowId => "###" + Title;

    public bool IsOpen { get; set; } = true;

    /// <summary>Open on first run and after Window > Reset Layout.</summary>
    public virtual bool OpenByDefault => true;

    /// <summary>Size the first time the window appears (panels outside the default layout float).</summary>
    protected virtual Vector2? DefaultSize => null;

    protected virtual ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.None;

    public void Draw()
    {
        if (!IsOpen)
        {
            OnHidden();
            return;
        }

        bool open = true;
        if (DefaultSize is { } size)
            ImGui.SetNextWindowSize(size, ImGuiCond.FirstUseEver);
        PushWindowStyle();
        bool visible = ImGui.Begin($"{Icon}  {Title}{WindowId}", ref open, WindowFlags);
        PopWindowStyle();

        if (visible)
            DrawContent();
        else
            OnHidden();
        ImGui.End();

        IsOpen = open;
    }

    protected abstract void DrawContent();

    /// <summary>Called on frames where the panel is closed, collapsed or behind another tab.</summary>
    protected virtual void OnHidden() { }

    /// <summary>Style pushed around ImGui.Begin (e.g. zero padding for the viewport).</summary>
    protected virtual void PushWindowStyle() { }

    protected virtual void PopWindowStyle() { }

    protected static void Placeholder(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }
}

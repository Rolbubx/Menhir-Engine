using ImGuiNET;

namespace Menhir.Editor.Panels;

/// <summary>
/// A dockable editor window. The title doubles as the ImGui window ID, which is
/// what the dock layout (and layout.ini) refers to, so keep it stable.
/// </summary>
public abstract class EditorPanel
{
    public abstract string Title { get; }

    public bool IsOpen { get; set; } = true;

    protected virtual ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.None;

    public void Draw()
    {
        if (!IsOpen)
            return;

        bool open = true;
        PushWindowStyle();
        bool visible = ImGui.Begin(Title, ref open, WindowFlags);
        PopWindowStyle();

        if (visible)
            DrawContent();
        ImGui.End();

        IsOpen = open;
    }

    protected abstract void DrawContent();

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

using System.Numerics;
using ImGuiNET;

namespace Menhir.Editor.UI.Dialogs;

/// <summary>
/// A modal popup drawn by the application every frame. Call <see cref="RequestOpen"/>
/// from anywhere; the popup opens on the next <see cref="Draw"/>.
/// </summary>
public abstract class ModalDialog
{
    private bool _openRequested;

    /// <summary>Also the popup ID, so it must be unique among dialogs.</summary>
    protected abstract string Title { get; }

    /// <summary>Size when the dialog appears, or null to fit the content.</summary>
    protected virtual Vector2? InitialSize => null;

    /// <summary>True while any popup or modal is open, so global shortcuts can stay quiet.</summary>
    public static bool AnyOpen => ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);

    protected void RequestOpen() => _openRequested = true;

    public void Draw()
    {
        if (_openRequested)
        {
            ImGui.OpenPopup(Title);
            _openRequested = false;
        }

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f));
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoSavedSettings;
        if (InitialSize is { } size)
            ImGui.SetNextWindowSize(size, ImGuiCond.Appearing);
        else
            flags |= ImGuiWindowFlags.AlwaysAutoResize;

        bool open = true;
        if (!ImGui.BeginPopupModal(Title, ref open, flags))
            return;
        DrawContent();
        ImGui.EndPopup();
    }

    protected abstract void DrawContent();

    protected static void Close() => ImGui.CloseCurrentPopup();

    protected static void ErrorText(string? error)
    {
        if (error != null)
            ImGui.TextColored(new Vector4(1f, 0.45f, 0.4f, 1f), error);
    }

    /// <summary>A button that is greyed out and does nothing when <paramref name="enabled"/> is false.</summary>
    protected static bool Button(string label, bool enabled = true)
    {
        ImGui.BeginDisabled(!enabled);
        bool pressed = ImGui.Button(label);
        ImGui.EndDisabled();
        return pressed && enabled;
    }
}

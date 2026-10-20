using System.Numerics;
using ImGuiNET;

namespace Menhir.Editor.UI;

/// <summary>Dark, low-contrast theme in the spirit of the Unity editor.</summary>
public static class EditorTheme
{
    public static void Apply()
    {
        ImGui.StyleColorsDark();
        ImGuiStylePtr style = ImGui.GetStyle();

        style.WindowPadding = new Vector2(8, 6);
        style.FramePadding = new Vector2(6, 3);
        style.ItemSpacing = new Vector2(6, 4);
        style.WindowRounding = 2f;
        style.FrameRounding = 2f;
        style.TabRounding = 2f;
        style.ScrollbarRounding = 2f;
        style.GrabRounding = 2f;
        style.WindowBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.TabBorderSize = 0f;
        style.WindowMenuButtonPosition = ImGuiDir.None;

        var c = style.Colors;
        Vector4 bg = Rgb(0x1E, 0x1E, 0x1E);
        Vector4 panel = Rgb(0x28, 0x28, 0x28);
        Vector4 frame = Rgb(0x3A, 0x3A, 0x3A);
        Vector4 hover = Rgb(0x48, 0x48, 0x48);
        Vector4 active = Rgb(0x55, 0x55, 0x55);
        Vector4 accent = Rgb(0x2C, 0x5D, 0x87);
        Vector4 accentHover = Rgb(0x3A, 0x79, 0xB0);

        c[(int)ImGuiCol.Text] = Rgb(0xD2, 0xD2, 0xD2);
        c[(int)ImGuiCol.TextDisabled] = Rgb(0x80, 0x80, 0x80);
        c[(int)ImGuiCol.WindowBg] = panel;
        c[(int)ImGuiCol.ChildBg] = panel;
        c[(int)ImGuiCol.PopupBg] = Rgb(0x25, 0x25, 0x25);
        c[(int)ImGuiCol.Border] = Rgb(0x14, 0x14, 0x14);
        c[(int)ImGuiCol.BorderShadow] = Vector4.Zero;
        c[(int)ImGuiCol.FrameBg] = frame;
        c[(int)ImGuiCol.FrameBgHovered] = hover;
        c[(int)ImGuiCol.FrameBgActive] = active;
        c[(int)ImGuiCol.TitleBg] = bg;
        c[(int)ImGuiCol.TitleBgActive] = bg;
        c[(int)ImGuiCol.TitleBgCollapsed] = bg;
        c[(int)ImGuiCol.MenuBarBg] = Rgb(0x19, 0x19, 0x19);
        c[(int)ImGuiCol.ScrollbarBg] = bg;
        c[(int)ImGuiCol.CheckMark] = accentHover;
        c[(int)ImGuiCol.SliderGrab] = accent;
        c[(int)ImGuiCol.SliderGrabActive] = accentHover;
        c[(int)ImGuiCol.Button] = frame;
        c[(int)ImGuiCol.ButtonHovered] = hover;
        c[(int)ImGuiCol.ButtonActive] = active;
        c[(int)ImGuiCol.Header] = accent;
        c[(int)ImGuiCol.HeaderHovered] = hover;
        c[(int)ImGuiCol.HeaderActive] = accentHover;
        c[(int)ImGuiCol.Separator] = Rgb(0x14, 0x14, 0x14);
        c[(int)ImGuiCol.SeparatorHovered] = accent;
        c[(int)ImGuiCol.SeparatorActive] = accentHover;
        c[(int)ImGuiCol.ResizeGrip] = Vector4.Zero;
        c[(int)ImGuiCol.ResizeGripHovered] = accent;
        c[(int)ImGuiCol.ResizeGripActive] = accentHover;
        c[(int)ImGuiCol.Tab] = bg;
        c[(int)ImGuiCol.TabHovered] = hover;
        c[(int)ImGuiCol.TabActive] = panel;
        c[(int)ImGuiCol.TabUnfocused] = bg;
        c[(int)ImGuiCol.TabUnfocusedActive] = panel;
        c[(int)ImGuiCol.DockingPreview] = accentHover with { W = 0.6f };
        c[(int)ImGuiCol.DockingEmptyBg] = bg;
        c[(int)ImGuiCol.NavHighlight] = accentHover;
    }

    private static Vector4 Rgb(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f, 1f);
}

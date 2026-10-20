using System.Numerics;
using ImGuiNET;

namespace Menhir.Editor.UI;

/// <summary>Dark theme modeled on the Unity editor, with rounded corners.</summary>
public static class EditorTheme
{
    // Unity's dark skin.
    public static readonly Vector4 Background = Rgb(0x19, 0x19, 0x19); // behind the panels, menu and status bar
    public static readonly Vector4 TabBar = Rgb(0x28, 0x28, 0x28);
    public static readonly Vector4 Panel = Rgb(0x38, 0x38, 0x38);
    public static readonly Vector4 Field = Rgb(0x2A, 0x2A, 0x2A);
    public static readonly Vector4 FieldHover = Rgb(0x30, 0x30, 0x30);
    public static readonly Vector4 Button = Rgb(0x58, 0x58, 0x58);
    public static readonly Vector4 ButtonHover = Rgb(0x67, 0x67, 0x67);
    public static readonly Vector4 ButtonPressed = Rgb(0x46, 0x46, 0x46);
    public static readonly Vector4 HeaderBar = Rgb(0x3E, 0x3E, 0x3E); // inspector component headers
    public static readonly Vector4 HeaderBarHover = Rgb(0x45, 0x45, 0x45);
    public static readonly Vector4 Line = Rgb(0x1E, 0x1E, 0x1E);
    public static readonly Vector4 Accent = Rgb(0x2C, 0x5D, 0x87); // selection
    public static readonly Vector4 AccentBright = Rgb(0x3A, 0x79, 0xBB);
    public static readonly Vector4 SelectionUnfocused = Rgb(0x4D, 0x4D, 0x4D);
    public static readonly Vector4 Text = Rgb(0xD2, 0xD2, 0xD2);
    public static readonly Vector4 TextDim = Rgb(0x8A, 0x8A, 0x8A);
    public static readonly Vector4 Warning = Rgb(0xF2, 0xC1, 0x4A);
    public static readonly Vector4 Error = Rgb(0xF0, 0x5A, 0x4F);

    public static void Apply()
    {
        ImGui.StyleColorsDark();
        ImGuiStylePtr style = ImGui.GetStyle();

        style.WindowPadding = new Vector2(8, 6);
        style.FramePadding = new Vector2(6, 3);
        style.ItemSpacing = new Vector2(6, 4);
        style.ItemInnerSpacing = new Vector2(4, 4);
        style.IndentSpacing = 14f;
        style.ScrollbarSize = 11f;
        style.GrabMinSize = 9f;

        style.WindowRounding = 6f;
        style.ChildRounding = 4f;
        style.FrameRounding = 4f;
        style.PopupRounding = 5f;
        style.ScrollbarRounding = 6f;
        style.GrabRounding = 3f;
        style.TabRounding = 5f;

        style.WindowBorderSize = 1f;
        style.ChildBorderSize = 1f;
        style.PopupBorderSize = 1f;
        style.FrameBorderSize = 1f;
        style.TabBorderSize = 0f;
        style.TabBarBorderSize = 1f;
        style.DockingSeparatorSize = 2f;
        style.WindowMenuButtonPosition = ImGuiDir.None;
        // Only the active tab shows its close button, like Unity.
        style.TabMinWidthForCloseButton = float.MaxValue;
        style.SeparatorTextBorderSize = 1f;

        var c = style.Colors;
        c[(int)ImGuiCol.Text] = Text;
        c[(int)ImGuiCol.TextDisabled] = TextDim;
        c[(int)ImGuiCol.TextSelectedBg] = AccentBright with { W = 0.55f };
        c[(int)ImGuiCol.WindowBg] = Panel;
        c[(int)ImGuiCol.ChildBg] = Vector4.Zero;
        c[(int)ImGuiCol.PopupBg] = Rgb(0x30, 0x30, 0x30);
        c[(int)ImGuiCol.Border] = Rgb(0x23, 0x23, 0x23);
        c[(int)ImGuiCol.BorderShadow] = Vector4.Zero;
        c[(int)ImGuiCol.FrameBg] = Field;
        c[(int)ImGuiCol.FrameBgHovered] = FieldHover;
        c[(int)ImGuiCol.FrameBgActive] = Rgb(0x26, 0x26, 0x26);
        c[(int)ImGuiCol.TitleBg] = TabBar;
        c[(int)ImGuiCol.TitleBgActive] = TabBar;
        c[(int)ImGuiCol.TitleBgCollapsed] = TabBar;
        c[(int)ImGuiCol.MenuBarBg] = Background;
        c[(int)ImGuiCol.ScrollbarBg] = Vector4.Zero;
        c[(int)ImGuiCol.ScrollbarGrab] = Rgb(0x5A, 0x5A, 0x5A);
        c[(int)ImGuiCol.ScrollbarGrabHovered] = Rgb(0x6A, 0x6A, 0x6A);
        c[(int)ImGuiCol.ScrollbarGrabActive] = Rgb(0x7A, 0x7A, 0x7A);
        c[(int)ImGuiCol.CheckMark] = Text;
        c[(int)ImGuiCol.SliderGrab] = Rgb(0x99, 0x99, 0x99);
        c[(int)ImGuiCol.SliderGrabActive] = Text;
        c[(int)ImGuiCol.Button] = Button;
        c[(int)ImGuiCol.ButtonHovered] = ButtonHover;
        c[(int)ImGuiCol.ButtonActive] = ButtonPressed;
        c[(int)ImGuiCol.Header] = Accent;
        c[(int)ImGuiCol.HeaderHovered] = Rgb(0x45, 0x45, 0x45);
        c[(int)ImGuiCol.HeaderActive] = AccentBright;
        c[(int)ImGuiCol.Separator] = Line;
        c[(int)ImGuiCol.SeparatorHovered] = AccentBright;
        c[(int)ImGuiCol.SeparatorActive] = AccentBright;
        c[(int)ImGuiCol.ResizeGrip] = Vector4.Zero;
        c[(int)ImGuiCol.ResizeGripHovered] = AccentBright with { W = 0.6f };
        c[(int)ImGuiCol.ResizeGripActive] = AccentBright;
        c[(int)ImGuiCol.Tab] = TabBar;
        c[(int)ImGuiCol.TabHovered] = Rgb(0x48, 0x48, 0x48);
        c[(int)ImGuiCol.TabActive] = Panel;
        c[(int)ImGuiCol.TabUnfocused] = TabBar;
        c[(int)ImGuiCol.TabUnfocusedActive] = Panel;
        c[(int)ImGuiCol.DockingPreview] = AccentBright with { W = 0.5f };
        c[(int)ImGuiCol.DockingEmptyBg] = Background;
        c[(int)ImGuiCol.TableHeaderBg] = HeaderBar;
        c[(int)ImGuiCol.TableBorderStrong] = Line;
        c[(int)ImGuiCol.TableBorderLight] = Rgb(0x2E, 0x2E, 0x2E);
        c[(int)ImGuiCol.TableRowBg] = Vector4.Zero;
        c[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1f, 1f, 1f, 0.025f);
        c[(int)ImGuiCol.NavHighlight] = AccentBright;
        c[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0f, 0f, 0f, 0.45f);
        c[(int)ImGuiCol.PlotHistogram] = AccentBright;
    }

    /// <summary>
    /// Selected rows are blue in the focused panel and grey in the others, like Unity.
    /// Call at the start of a panel's content; pair with <see cref="PopSelectionColors"/>.
    /// </summary>
    public static void PushSelectionColors()
    {
        bool focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        ImGui.PushStyleColor(ImGuiCol.Header, focused ? Accent : SelectionUnfocused);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, focused ? AccentBright : SelectionUnfocused);
    }

    public static void PopSelectionColors() => ImGui.PopStyleColor(2);

    /// <summary>The blue "main action" button of a dialog (Save, Create, Open...).</summary>
    public static bool PrimaryButton(string label, Vector2 size = default)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AccentBright);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Rgb(0x25, 0x4E, 0x72));
        bool pressed = ImGui.Button(label, size);
        ImGui.PopStyleColor(3);
        return pressed;
    }

    /// <summary>A button without background until hovered, for toolbars and icon buttons.</summary>
    public static bool FlatButton(string label, Vector2 size = default)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);
        bool pressed = ImGui.Button(label, size);
        ImGui.PopStyleColor(2);
        return pressed;
    }

    /// <summary>A button that stays highlighted while <paramref name="value"/> is on. Returns true when toggled.</summary>
    public static bool ToggleButton(string label, ref bool value, Vector2 size = default)
    {
        if (value)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Rgb(0x46, 0x60, 0x7C));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Rgb(0x50, 0x6D, 0x8C));
        }
        bool clicked = ImGui.Button(label, size);
        if (value)
            ImGui.PopStyleColor(2);
        if (clicked)
            value = !value;
        return clicked;
    }

    public static Vector4 Rgb(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f, 1f);
}

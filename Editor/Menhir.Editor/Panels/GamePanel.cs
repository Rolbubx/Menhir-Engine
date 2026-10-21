using System.Numerics;
using ImGuiNET;
using Menhir.Editor.Input;
using Menhir.Editor.Playing;
using Menhir.Editor.Projects;
using Menhir.Editor.Rendering;
using Menhir.Editor.Scenes;
using Menhir.Editor.UI;
using Silk.NET.Input;

namespace Menhir.Editor.Panels;

/// <summary>
/// What the game's camera sees, like Unity's Game view: the open scene in Edit mode, the running game
/// in Play mode. While it has the focus in Play mode, the keyboard, mouse and first gamepad go to the
/// game; Escape gives the cursor back when the game captured it.
/// </summary>
public sealed class GamePanel : EditorPanel, IDisposable
{
    private readonly EditorContext _context;
    private readonly PlayMode _play;
    private readonly IInputContext _input;
    private readonly Action<bool> _setCursorCaptured;
    private readonly IRenderTarget _target;
    private readonly SceneRenderer _sceneRenderer;
    private static readonly RenderingSettings DefaultRendering = new();

    // Where the image was drawn last frame, and whether the panel had the focus.
    private Vector2 _origin;
    private Vector2 _size;
    private bool _focused;
    private bool _captured;
    private bool _captureReleased;
    private readonly bool[] _mouseHeld = new bool[InputCodes.MouseButtons];

    public GamePanel(EditorContext context, PlayMode play, IRenderer renderer, IInputContext input, Action<bool> setCursorCaptured)
    {
        _context = context;
        _play = play;
        _input = input;
        _setCursorCaptured = setCursorCaptured;
        _target = renderer.CreateRenderTarget(1, 1);
        _sceneRenderer = new SceneRenderer(renderer, ResolveAssetPath);
    }

    public override string Title => "Game";

    public override string Icon => Icons.Game;

    protected override ImGuiWindowFlags WindowFlags =>
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    protected override void PushWindowStyle() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    protected override void PopWindowStyle() => ImGui.PopStyleVar();

    /// <summary>Brings the panel to the front with the focus (when entering Play mode).</summary>
    public void Focus()
    {
        IsOpen = true;
        ImGui.SetWindowFocus(WindowId);
        _captureReleased = false;
    }

    protected override void DrawContent()
    {
        Vector2 size = ImGui.GetContentRegionAvail();
        if (size.X < 1 || size.Y < 1)
        {
            _focused = false;
            return;
        }

        _origin = ImGui.GetCursorScreenPos();
        _size = size;
        bool hovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(_origin, _origin + size);
        // Any button gives the focus (ImGui only does it for the left one), so a right-click look works at once.
        if (hovered && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right) ||
                        ImGui.IsMouseClicked(ImGuiMouseButton.Middle)))
        {
            ImGui.SetWindowFocus();
            _captureReleased = false;
        }
        _focused = ImGui.IsWindowFocused();

        IReadOnlyList<Scene> scenes = _play.Scenes ?? [_context.ActiveScene];
        GameView view = GameCamera.Compute(scenes, size.X / size.Y);
        var options = new ViewportOptions
        {
            Grid = false,
            Skybox = view.ClearFlags == CameraClearFlags.Skybox,
            Background = view.Background,
        };
        Vector2 pixelScale = ImGui.GetIO().DisplayFramebufferScale;
        _target.Resize((int)(size.X * pixelScale.X), (int)(size.Y * pixelScale.Y));
        _sceneRenderer.Render(scenes, _target,
            new ViewParams(view.View, view.Projection, view.Position, view.FieldOfViewY, view.Near, view.Far), options,
            _context.Project?.Settings.Rendering ?? DefaultRendering);
        ImGui.Image(_target.ColorTexture, size, new Vector2(0, 1), new Vector2(1, 0));

        if (_play.IsPlaying && _play.IsPaused)
            DrawLabel("Paused");
        else if (_play.IsPlaying && _captured)
            DrawLabel("Esc: free the cursor");
    }

    protected override void OnHidden() => _focused = false;

    private void DrawLabel(string label)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 padding = new(6, 3);
        Vector2 pos = _origin + new Vector2(8, 8);
        drawList.AddRectFilled(pos, pos + ImGui.CalcTextSize(label) + padding * 2, 0xB0000000, 3f);
        drawList.AddText(pos + padding, 0xFFFFFFFF, label);
    }

    /// <summary>
    /// The devices for the game's next frame: empty unless the panel has the focus in Play mode.
    /// Also captures the cursor while the game asks for it.
    /// </summary>
    public InputFrame ReadInput()
    {
        var frame = new InputFrame();
        bool active = _play.IsPlaying && _focused;
        if (active && _captured && ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            _captureReleased = true;
        SetCaptured(active && !_captureReleased && _play.Session!.CursorCaptured);
        if (!active)
            return frame;

        foreach (IKeyboard keyboard in _input.Keyboards)
        {
            foreach (Key key in keyboard.SupportedKeys)
            {
                // Silk's key values are GLFW's key codes, which are the game's.
                if (key != Key.Unknown && keyboard.IsKeyPressed(key))
                    frame.Down.Add((int)key);
            }
        }

        ImGuiIOPtr io = ImGui.GetIO();
        bool overImage = ImGui.IsMouseHoveringRect(_origin, _origin + _size);
        for (int b = 0; b < InputCodes.MouseButtons; b++)
        {
            // A press counts from the image (not the tab or the panel's edge) until it is released.
            bool down = _input.Mice.Any(m => m.IsButtonPressed((MouseButton)b));
            _mouseHeld[b] = down && (_mouseHeld[b] || overImage || _captured);
            if (_mouseHeld[b])
                frame.Down.Add(InputCodes.FirstMouseButton + b);
        }
        frame.MouseDeltaX = io.MouseDelta.X;
        frame.MouseDeltaY = io.MouseDelta.Y;
        frame.MouseX = io.MousePos.X - _origin.X;
        frame.MouseY = _size.Y - (io.MousePos.Y - _origin.Y);
        frame.Scroll = io.MouseWheel;

        if (_input.Gamepads.FirstOrDefault(g => g.IsConnected) is { } pad)
        {
            foreach (Button button in pad.Buttons)
            {
                // Silk's button names follow GLFW's gamepad buttons (A, B, X, Y, bumpers, Back, Start, Guide, sticks, D-pad).
                int index = (int)button.Name;
                if (button.Pressed && index >= 0 && index < InputCodes.GamepadButtons)
                    frame.Down.Add(InputCodes.FirstGamepadButton + index);
            }
            if (pad.Thumbsticks.Count > 0)
            {
                frame.GamepadAxes[0] = pad.Thumbsticks[0].X;
                frame.GamepadAxes[1] = pad.Thumbsticks[0].Y;
            }
            if (pad.Thumbsticks.Count > 1)
            {
                frame.GamepadAxes[2] = pad.Thumbsticks[1].X;
                frame.GamepadAxes[3] = pad.Thumbsticks[1].Y;
            }
            for (int t = 0; t < Math.Min(pad.Triggers.Count, 2); t++)
                frame.GamepadAxes[4 + t] = (pad.Triggers[t].Position + 1f) * 0.5f; // -1..1 to 0..1, like the player
        }
        return frame;
    }

    private void SetCaptured(bool captured)
    {
        if (captured == _captured)
            return;
        _captured = captured;
        _setCursorCaptured(captured);
    }

    private string? ResolveAssetPath(Guid guid) =>
        _context.Project is { } project && project.Assets.FindByGuid(guid) is { } asset
            ? project.Assets.ToAbsolutePath(asset.Path)
            : null;

    public void Dispose()
    {
        SetCaptured(false);
        _sceneRenderer.Dispose();
        _target.Dispose();
    }
}

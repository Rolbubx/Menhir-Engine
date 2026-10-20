using System.Numerics;
using ImGuiNET;
using Menhir.Editor.Projects;
using Menhir.Editor.Rendering;
using Menhir.Editor.UI;

namespace Menhir.Editor.Panels;

/// <summary>
/// Renders the active scene into an offscreen target shown as an ImGui image.
/// Hold the right mouse button to fly: mouse to look, WASD to move, Q/E down/up,
/// Shift to go faster, mouse wheel to change the speed.
/// </summary>
public sealed class ViewportPanel : EditorPanel, IDisposable
{
    private const float LookSensitivity = 0.0035f; // radians per pixel
    private const float MinSpeed = 0.1f;
    private const float MaxSpeed = 200f;
    private const float SpeedStep = 1.2f; // multiplier per wheel notch
    private const float BoostMultiplier = 3f;

    private readonly EditorContext _context;
    private readonly IRenderTarget _target;
    private readonly SceneRenderer _sceneRenderer;
    private readonly Action<bool> _setCursorCaptured;
    private readonly EditorCamera _camera = new();
    private readonly ViewportOptions _options = new();
    private static readonly RenderingSettings DefaultRendering = new();

    private bool _flying;
    private float _moveSpeed = 5f;
    private double _speedLabelUntil;

    public ViewportPanel(EditorContext context, IRenderer renderer, Action<bool> setCursorCaptured)
    {
        _context = context;
        _setCursorCaptured = setCursorCaptured;
        _target = renderer.CreateRenderTarget(1, 1);
        _sceneRenderer = new SceneRenderer(renderer, ResolveAssetPath);
    }

    public override string Title => "Viewport";

    public override string Icon => Icons.Viewport;

    protected override ImGuiWindowFlags WindowFlags =>
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    // The scene texture fills the whole window, edge to edge.
    protected override void PushWindowStyle() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    protected override void PopWindowStyle() => ImGui.PopStyleVar();

    protected override void DrawContent()
    {
        Vector2 size = ImGui.GetContentRegionAvail();
        if (size.X < 1 || size.Y < 1)
            return;

        Vector2 origin = ImGui.GetCursorScreenPos();
        bool hovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(origin, origin + size);
        UpdateCamera(hovered);

        Vector2 pixelScale = ImGui.GetIO().DisplayFramebufferScale;
        _target.Resize((int)(size.X * pixelScale.X), (int)(size.Y * pixelScale.Y));
        _sceneRenderer.Render(_context.ActiveScene, _target, _camera.GetViewParams(size.X / size.Y), _options,
            _context.Project?.Settings.Rendering ?? DefaultRendering, _context.SelectedEntityId);

        // GL textures start at the bottom-left, ImGui images at the top-left: flip V.
        ImGui.Image(_target.ColorTexture, size, new Vector2(0, 1), new Vector2(1, 0));

        DrawOverlay(origin);
        DrawEffectsMenu(origin, size);
    }

    private string? ResolveAssetPath(Guid guid) =>
        _context.Project is { } project && project.Assets.FindByGuid(guid) is { } asset
            ? project.Assets.ToAbsolutePath(asset.Path)
            : null;

    /// <summary>Top-right button to show or hide the skybox, post-processing and grid, like Unity's scene view.</summary>
    private void DrawEffectsMenu(Vector2 origin, Vector2 size)
    {
        const string label = "Effects";
        Vector2 buttonSize = new(ImGui.CalcTextSize($"{Icons.Layers}  {label}").X + ImGui.GetStyle().FramePadding.X * 2,
            ImGui.GetFrameHeight());
        ImGui.SetCursorScreenPos(origin + new Vector2(size.X - buttonSize.X - 8, 8));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.12f, 0.12f, 0.12f, 0.85f));
        if (ImGui.Button($"{Icons.Layers}  {label}", buttonSize))
            ImGui.OpenPopup("##viewportEffects");
        ImGui.PopStyleColor();

        if (!ImGui.BeginPopup("##viewportEffects"))
            return;
        bool skybox = _options.Skybox, post = _options.PostProcessing, grid = _options.Grid, shadows = _options.Shadows;
        if (ImGui.Checkbox("Skybox", ref skybox))
            _options.Skybox = skybox;
        if (ImGui.Checkbox("Shadows", ref shadows))
            _options.Shadows = shadows;
        if (ImGui.Checkbox("Post-processing", ref post))
            _options.PostProcessing = post;
        if (ImGui.Checkbox("Grid", ref grid))
            _options.Grid = grid;
        ImGui.EndPopup();
    }

    private void UpdateCamera(bool hovered)
    {
        ImGuiIOPtr io = ImGui.GetIO();

        if (!_flying && hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _flying = true;
            ImGui.SetWindowFocus();
            _setCursorCaptured(true);
        }
        else if (_flying && !ImGui.IsMouseDown(ImGuiMouseButton.Right))
        {
            StopFlying();
        }

        if ((hovered || _flying) && io.MouseWheel != 0)
        {
            _moveSpeed = Math.Clamp(_moveSpeed * MathF.Pow(SpeedStep, io.MouseWheel), MinSpeed, MaxSpeed);
            _speedLabelUntil = ImGui.GetTime() + 1.5;
        }

        if (!_flying)
            return;

        Vector2 look = io.MouseDelta;
        _camera.Yaw -= look.X * LookSensitivity;
        _camera.Pitch -= look.Y * LookSensitivity;

        Vector3 move = Vector3.Zero;
        if (ImGui.IsKeyDown(ImGuiKey.W)) move += _camera.Forward;
        if (ImGui.IsKeyDown(ImGuiKey.S)) move -= _camera.Forward;
        if (ImGui.IsKeyDown(ImGuiKey.D)) move += _camera.Right;
        if (ImGui.IsKeyDown(ImGuiKey.A)) move -= _camera.Right;
        if (ImGui.IsKeyDown(ImGuiKey.E)) move += Vector3.UnitY;
        if (ImGui.IsKeyDown(ImGuiKey.Q)) move -= Vector3.UnitY;

        if (move != Vector3.Zero)
        {
            float speed = _moveSpeed * (io.KeyShift ? BoostMultiplier : 1f);
            _camera.Position += Vector3.Normalize(move) * speed * io.DeltaTime;
        }
    }

    protected override void OnHidden()
    {
        if (_flying)
            StopFlying();
    }

    private void StopFlying()
    {
        _flying = false;
        _setCursorCaptured(false);
    }

    private void DrawOverlay(Vector2 origin)
    {
        if (ImGui.GetTime() > _speedLabelUntil)
            return;
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        string label = $"Speed: {_moveSpeed:0.##} m/s";
        Vector2 padding = new(6, 3);
        Vector2 pos = origin + new Vector2(8, 8);
        drawList.AddRectFilled(pos, pos + ImGui.CalcTextSize(label) + padding * 2, 0xB0000000, 3f);
        drawList.AddText(pos + padding, 0xFFFFFFFF, label);
    }

    public void Dispose()
    {
        if (_flying)
            StopFlying();
        _sceneRenderer.Dispose();
        _target.Dispose();
    }
}

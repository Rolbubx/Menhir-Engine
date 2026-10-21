using Menhir.Editor.Inspection;

namespace Menhir.Editor.Scenes;

/// <summary>
/// Built-in behaviour: flies the entity with the Input Manager's axes, like the viewport camera
/// (look with the mouse while the right button is held, move with WASD or the arrows). Runs in the
/// built game; the editor will run it in Play mode.
/// </summary>
[ComponentCategory("Gameplay")]
public sealed class FlyCamera : Component
{
    [Min(0)]
    [Tooltip("Meters per second.")]
    public float MoveSpeed { get; set; } = 5f;

    [Min(0)]
    [Tooltip("Degrees per unit of the look axes (Mouse X: 0.1 per pixel by default).")]
    public float LookSpeed { get; set; } = 2f;

    [Min(1)]
    [Tooltip("Speed multiplier while the Fast button is held.")]
    public float FastMultiplier { get; set; } = 3f;

    public string HorizontalAxis { get; set; } = "Horizontal";
    public string VerticalAxis { get; set; } = "Vertical";
    public string LookXAxis { get; set; } = "Mouse X";
    public string LookYAxis { get; set; } = "Mouse Y";
    public string FastButton { get; set; } = "Fire3";

    [Tooltip("Look only while the right mouse button is held (the cursor is hidden meanwhile).")]
    public bool RequireRightMouseButton { get; set; } = true;
}

using System.Numerics;

namespace Menhir.Editor.Rendering;

/// <summary>
/// Free-fly camera. Yaw 0 / pitch 0 looks down -Z; positive yaw turns left, positive pitch looks up.
/// </summary>
public sealed class EditorCamera
{
    private const float MaxPitch = 89f * MathF.PI / 180f;

    private float _pitch;

    public Vector3 Position { get; set; } = new(6, 4, 8);
    public float Yaw { get; set; } = 35f * MathF.PI / 180f;

    public float Pitch
    {
        get => _pitch;
        set => _pitch = Math.Clamp(value, -MaxPitch, MaxPitch);
    }

    public float FieldOfViewY { get; set; } = 60f * MathF.PI / 180f;
    public float Near { get; set; } = 0.05f;
    public float Far { get; set; } = 1000f;

    public EditorCamera()
    {
        Pitch = -22f * MathF.PI / 180f;
    }

    public Vector3 Forward => new(
        -MathF.Sin(Yaw) * MathF.Cos(Pitch),
        MathF.Sin(Pitch),
        -MathF.Cos(Yaw) * MathF.Cos(Pitch));

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Position + Forward, Vector3.UnitY);

    /// <summary>
    /// OpenGL-style projection (clip depth -1..1). System.Numerics' own perspective
    /// helpers target a 0..1 depth range, which would waste half of GL's depth precision.
    /// </summary>
    public Matrix4x4 GetProjection(float aspect)
    {
        float f = 1f / MathF.Tan(FieldOfViewY / 2f);
        return new Matrix4x4(
            f / aspect, 0, 0, 0,
            0, f, 0, 0,
            0, 0, (Far + Near) / (Near - Far), -1,
            0, 0, 2f * Far * Near / (Near - Far), 0);
    }

    public ViewParams GetViewParams(float aspect) => new(View, GetProjection(aspect), Position);
}

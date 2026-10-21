using System.Numerics;
using Menhir.Editor.Input;
using Menhir.Editor.Mathematics;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Runtime;

/// <summary>
/// What <see cref="FlyCamera"/> does each frame (the game's mh_behaviours.c does the same): yaw and
/// pitch from the look axes, then a move along the entity's own axes. Roll is dropped.
/// </summary>
public sealed class FlyCameraBehaviour
{
    public const int RightMouseButton = InputCodes.FirstMouseButton + 1;

    private readonly FlyCamera _settings;
    private readonly Transform _transform;

    public FlyCameraBehaviour(FlyCamera settings, Transform transform)
    {
        _settings = settings;
        _transform = transform;
        Vector3 euler = EulerAngles.FromQuaternion(transform.Rotation);
        Pitch = euler.X;
        Yaw = euler.Y;
    }

    /// <summary>Degrees.</summary>
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }

    /// <summary>The cursor is hidden while the camera looks around.</summary>
    public bool IsLooking { get; private set; }

    public void Update(InputState input, float deltaTime)
    {
        FlyCamera s = _settings;
        IsLooking = !s.RequireRightMouseButton || input.GetKey(RightMouseButton);
        if (IsLooking)
        {
            Yaw -= input.GetAxis(s.LookXAxis) * s.LookSpeed;
            Pitch = Math.Clamp(Pitch + input.GetAxis(s.LookYAxis) * s.LookSpeed, -89f, 89f);
        }
        float toRadians = MathF.PI / 180f;
        Quaternion rotation = Quaternion.CreateFromYawPitchRoll(Yaw * toRadians, Pitch * toRadians, 0f);
        _transform.Rotation = rotation;

        Matrix4x4 axes = Matrix4x4.CreateFromQuaternion(rotation);
        Vector3 move = Vector3.TransformNormal(Vector3.UnitX, axes) * input.GetAxis(s.HorizontalAxis) +
                       Vector3.TransformNormal(-Vector3.UnitZ, axes) * input.GetAxis(s.VerticalAxis);
        if (move.LengthSquared() > 1f)
            move = Vector3.Normalize(move);
        float speed = s.MoveSpeed * (input.GetButton(s.FastButton) ? MathF.Max(s.FastMultiplier, 1f) : 1f);
        _transform.Position += move * (speed * deltaTime);
    }
}

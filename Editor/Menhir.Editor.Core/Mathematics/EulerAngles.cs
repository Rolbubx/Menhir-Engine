using System.Numerics;

namespace Menhir.Editor.Mathematics;

/// <summary>
/// Euler angles in degrees, as shown in the inspector: X = pitch, Y = yaw, Z = roll.
/// Rotations are applied around Z, then X, then Y (same order as Unity), which is
/// the order of <see cref="Quaternion.CreateFromYawPitchRoll"/>.
/// </summary>
public static class EulerAngles
{
    public static Quaternion ToQuaternion(Vector3 degrees) =>
        Quaternion.CreateFromYawPitchRoll(
            DegreesToRadians(degrees.Y), DegreesToRadians(degrees.X), DegreesToRadians(degrees.Z));

    /// <summary>
    /// Returns one set of angles for the rotation. Pitch is in [-90, 90], yaw and roll in (-180, 180].
    /// At ±90° pitch, yaw and roll turn around the same axis; yaw is then reported as 0.
    /// </summary>
    public static Vector3 FromQuaternion(Quaternion rotation)
    {
        // With row vectors the rotation matrix is Rz * Rx * Ry, which gives:
        //   M32 = -sin(pitch)
        //   M31 = cos(pitch) sin(yaw),  M33 = cos(pitch) cos(yaw)
        //   M12 = sin(roll) cos(pitch), M22 = cos(roll) cos(pitch)
        Matrix4x4 m = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation));

        float sinPitch = Math.Clamp(-m.M32, -1f, 1f);
        float pitch = MathF.Asin(sinPitch);
        float yaw, roll;
        if (MathF.Abs(sinPitch) < 0.99999f)
        {
            yaw = MathF.Atan2(m.M31, m.M33);
            roll = MathF.Atan2(m.M12, m.M22);
        }
        else
        {
            // Gimbal lock: with yaw = 0, M11 = cos(roll) and M21 = -sin(roll).
            yaw = 0;
            roll = MathF.Atan2(-m.M21, m.M11);
        }

        return new Vector3(Clean(RadiansToDegrees(pitch)), Clean(RadiansToDegrees(yaw)), Clean(RadiansToDegrees(roll)));
    }

    /// <summary>Snaps float noise to 0 so the inspector shows "0" rather than "-0" or "1e-6".</summary>
    private static float Clean(float degrees) => MathF.Abs(degrees) < 1e-4f ? 0f : degrees;

    private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180f);

    private static float RadiansToDegrees(float radians) => radians * (180f / MathF.PI);
}

using System.Numerics;

namespace Menhir.Editor.Mathematics;

/// <summary>
/// Projection matrices with a -1..1 clip depth range (OpenGL's), in System.Numerics' row-vector
/// order. System.Numerics' own helpers target a 0..1 depth range.
/// </summary>
public static class Projections
{
    public static Matrix4x4 Perspective(float fieldOfViewY, float aspect, float near, float far)
    {
        float f = 1f / MathF.Tan(fieldOfViewY / 2f);
        return new Matrix4x4(
            f / aspect, 0, 0, 0,
            0, f, 0, 0,
            0, 0, (far + near) / (near - far), -1,
            0, 0, 2f * far * near / (near - far), 0);
    }

    /// <summary>A box from -halfWidth to halfWidth (x), -halfHeight to halfHeight (y), near to far in front (-z).</summary>
    public static Matrix4x4 Orthographic(float halfWidth, float halfHeight, float near, float far) => new(
        1f / halfWidth, 0, 0, 0,
        0, 1f / halfHeight, 0, 0,
        0, 0, -2f / (far - near), 0,
        0, 0, -(far + near) / (far - near), 1);
}

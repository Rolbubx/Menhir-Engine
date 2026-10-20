using System.Numerics;

namespace Menhir.Editor.Mathematics;

/// <summary>An axis-aligned box.</summary>
public readonly record struct Bounds(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) * 0.5f;

    public Bounds Encapsulate(Bounds other) => new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    public IEnumerable<Vector3> Corners()
    {
        for (int i = 0; i < 8; i++)
            yield return new Vector3((i & 1) == 0 ? Min.X : Max.X, (i & 2) == 0 ? Min.Y : Max.Y, (i & 4) == 0 ? Min.Z : Max.Z);
    }

    /// <summary>The box around <paramref name="local"/> once transformed by <paramref name="matrix"/>.</summary>
    public static Bounds Transform(Bounds local, Matrix4x4 matrix)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (Vector3 corner in local.Corners())
        {
            Vector3 p = Vector3.Transform(corner, matrix);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return new Bounds(min, max);
    }
}

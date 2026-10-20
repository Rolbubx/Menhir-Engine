using System.Numerics;

namespace Menhir.Editor.Rendering;

public static class Primitives
{
    /// <summary>Unit cube centered on the origin, with per-face normals (24 vertices).</summary>
    public static MeshData CreateCube()
    {
        Vector3[] faceNormals =
        [
            Vector3.UnitX, -Vector3.UnitX,
            Vector3.UnitY, -Vector3.UnitY,
            Vector3.UnitZ, -Vector3.UnitZ,
        ];

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<uint>();

        foreach (Vector3 n in faceNormals)
        {
            // u × v == n, so the corners below wind counter-clockwise seen from outside.
            Vector3 u = MathF.Abs(n.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            Vector3 v = Vector3.Cross(n, u);

            uint first = (uint)positions.Count;
            positions.Add((n - u - v) * 0.5f);
            positions.Add((n + u - v) * 0.5f);
            positions.Add((n + u + v) * 0.5f);
            positions.Add((n - u + v) * 0.5f);
            normals.AddRange([n, n, n, n]);
            indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }

        return new MeshData
        {
            Positions = positions.ToArray(),
            Normals = normals.ToArray(),
            Indices = indices.ToArray(),
        };
    }
}

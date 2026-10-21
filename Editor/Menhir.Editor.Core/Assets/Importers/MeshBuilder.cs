using System.Numerics;
using Menhir.Editor.Rendering;

namespace Menhir.Editor.Assets.Importers;

/// <summary>
/// Collects triangles (positions, optional normals) and builds a <see cref="MeshData"/>. Corners
/// without a normal get one from the faces around their position that are within
/// <see cref="SmoothingAngle"/> of their own face (Unity's default import), so hard edges stay hard.
/// </summary>
internal sealed class MeshBuilder
{
    public const float SmoothingAngle = 60f;

    private readonly List<(Vector3 Position, Vector3? Normal)> _corners = new();

    public int TriangleCount => _corners.Count / 3;

    public void AddTriangle((Vector3 Position, Vector3? Normal) a, (Vector3 Position, Vector3? Normal) b,
        (Vector3 Position, Vector3? Normal) c)
    {
        _corners.Add(a);
        _corners.Add(b);
        _corners.Add(c);
    }

    public MeshData Build()
    {
        int count = _corners.Count;
        var faceNormals = new Vector3[count / 3];   // area-weighted
        for (int t = 0; t < faceNormals.Length; t++)
        {
            Vector3 a = _corners[t * 3].Position, b = _corners[t * 3 + 1].Position, c = _corners[t * 3 + 2].Position;
            faceNormals[t] = Vector3.Cross(b - a, c - a) * 0.5f;
        }

        // Faces touching each position, to smooth the corners that have no normal.
        var facesAt = new Dictionary<Vector3, List<int>>();
        for (int i = 0; i < count; i++)
        {
            if (_corners[i].Normal != null)
                continue;
            if (!facesAt.TryGetValue(_corners[i].Position, out List<int>? faces))
                facesAt[_corners[i].Position] = faces = new List<int>();
            faces.Add(i / 3);
        }

        float cosLimit = MathF.Cos(SmoothingAngle * MathF.PI / 180f);
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new uint[count];
        var unique = new Dictionary<(Vector3, Vector3), uint>();
        for (int i = 0; i < count; i++)
        {
            (Vector3 position, Vector3? given) = _corners[i];
            Vector3 normal;
            if (given is { } n && n.LengthSquared() > 1e-12f)
                normal = Vector3.Normalize(n);
            else
            {
                Vector3 own = SafeNormalize(faceNormals[i / 3]);
                Vector3 sum = Vector3.Zero;
                foreach (int face in facesAt.GetValueOrDefault(position) ?? [i / 3])
                {
                    if (Vector3.Dot(SafeNormalize(faceNormals[face]), own) >= cosLimit - 1e-5f)
                        sum += faceNormals[face];
                }
                normal = sum.LengthSquared() > 1e-20f ? Vector3.Normalize(sum) : own;
            }

            if (!unique.TryGetValue((position, normal), out uint index))
            {
                index = (uint)positions.Count;
                positions.Add(position);
                normals.Add(normal);
                unique[(position, normal)] = index;
            }
            indices[i] = index;
        }
        return new MeshData { Positions = positions.ToArray(), Normals = normals.ToArray(), Indices = indices };
    }

    private static Vector3 SafeNormalize(Vector3 v) => v.LengthSquared() > 1e-20f ? Vector3.Normalize(v) : Vector3.UnitY;
}

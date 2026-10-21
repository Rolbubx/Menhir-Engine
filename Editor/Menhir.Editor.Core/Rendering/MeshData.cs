using System.Numerics;
using Menhir.Editor.Mathematics;

namespace Menhir.Editor.Rendering;

/// <summary>API-agnostic triangle mesh (counter-clockwise front faces).</summary>
public sealed class MeshData
{
    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }
    public required uint[] Indices { get; init; }

    /// <summary>The box around the vertices (a zero box for an empty mesh).</summary>
    public Bounds Bounds
    {
        get
        {
            if (Positions.Length == 0)
                return new Bounds(Vector3.Zero, Vector3.Zero);
            Vector3 min = Positions[0], max = Positions[0];
            foreach (Vector3 p in Positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            return new Bounds(min, max);
        }
    }
}

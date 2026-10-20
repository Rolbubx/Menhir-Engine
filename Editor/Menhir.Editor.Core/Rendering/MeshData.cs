using System.Numerics;

namespace Menhir.Editor.Rendering;

/// <summary>API-agnostic triangle mesh (counter-clockwise front faces).</summary>
public sealed class MeshData
{
    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }
    public required uint[] Indices { get; init; }
}

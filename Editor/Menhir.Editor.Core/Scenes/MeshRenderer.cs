using System.Numerics;
using Menhir.Editor.Inspection;

namespace Menhir.Editor.Scenes;

/// <summary>Built-in meshes, until asset importing exists.</summary>
public enum PrimitiveMesh
{
    Cube,
}

/// <summary>Draws a mesh at the entity's transform.</summary>
public sealed class MeshRenderer : Component
{
    public PrimitiveMesh Mesh { get; set; } = PrimitiveMesh.Cube;

    /// <summary>Linear RGBA color.</summary>
    [Color]
    public Vector4 Color { get; set; } = new(0.6f, 0.6f, 0.6f, 1f);
}

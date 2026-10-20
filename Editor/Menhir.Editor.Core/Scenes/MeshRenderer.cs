using System.Numerics;
using Menhir.Editor.Inspection;

namespace Menhir.Editor.Scenes;

/// <summary>Built-in meshes, until asset importing exists.</summary>
public enum PrimitiveMesh
{
    Cube,
}

/// <summary>Draws a mesh at the entity's transform, shaded like URP's Lit material.</summary>
[ComponentCategory("Rendering")]
public sealed class MeshRenderer : Component
{
    public PrimitiveMesh Mesh { get; set; } = PrimitiveMesh.Cube;

    /// <summary>Linear RGBA color.</summary>
    [Color]
    public Vector4 Color { get; set; } = new(0.6f, 0.6f, 0.6f, 1f);

    [Range(0, 1)]
    public float Metallic { get; set; }

    [Range(0, 1)]
    public float Smoothness { get; set; } = 0.5f;

    public bool CastShadows { get; set; } = true;

    public bool ReceiveShadows { get; set; } = true;
}

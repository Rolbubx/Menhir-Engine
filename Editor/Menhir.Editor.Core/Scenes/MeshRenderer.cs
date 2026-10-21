using System.Numerics;
using Menhir.Editor.Inspection;

namespace Menhir.Editor.Scenes;

/// <summary>Built-in meshes, until asset importing exists.</summary>
public enum PrimitiveMesh
{
    Cube,
}

/// <summary>
/// Draws a mesh at the entity's transform, shaded like URP's Lit material: an imported model
/// (<see cref="Model"/>) or else a built-in mesh.
/// </summary>
[ComponentCategory("Rendering")]
public sealed class MeshRenderer : Component
{
    [Tooltip("Built-in mesh, drawn when no Model is set.")]
    public PrimitiveMesh Mesh { get; set; } = PrimitiveMesh.Cube;

    [ModelAsset]
    [Tooltip("An imported model (.obj, .gltf, .glb); replaces Mesh.")]
    public Guid? Model { get; set; }

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

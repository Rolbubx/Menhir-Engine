using System.Numerics;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Rendering;

/// <summary>Draws a <see cref="Scene"/> through an <see cref="IRenderer"/>.</summary>
public sealed class SceneRenderer : IDisposable
{
    private static readonly Vector4 ClearColor = new(0.19f, 0.2f, 0.22f, 1f);
    private static readonly Vector4 SelectionColor = new(1f, 0.6f, 0.1f, 1f);

    private readonly IRenderer _renderer;
    private readonly Dictionary<PrimitiveMesh, IMesh> _primitives = new();

    public SceneRenderer(IRenderer renderer)
    {
        _renderer = renderer;
        _primitives[PrimitiveMesh.Cube] = renderer.CreateMesh(Primitives.CreateCube());
    }

    /// <param name="selectedEntityId">Entity drawn with a selection outline, 0 for none.</param>
    public void Render(Scene scene, IRenderTarget target, in ViewParams view, long selectedEntityId = 0)
    {
        _renderer.BeginPass(target, view, ClearColor);

        foreach (Entity entity in scene.Entities)
        {
            if (entity.GetComponent<MeshRenderer>() is not { } meshRenderer)
                continue;
            Matrix4x4 world = scene.GetWorldMatrix(entity);
            IMesh mesh = _primitives[meshRenderer.Mesh];
            _renderer.DrawMesh(mesh, world, meshRenderer.Color);
            if (entity.LocalId == selectedEntityId)
                _renderer.DrawOutline(mesh, world, SelectionColor);
        }

        _renderer.DrawGrid();
        _renderer.EndPass();
    }

    public void Dispose()
    {
        foreach (IMesh mesh in _primitives.Values)
            mesh.Dispose();
        _primitives.Clear();
    }
}

using System.Numerics;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Rendering;

/// <summary>Draws a <see cref="Scene"/> through an <see cref="IRenderer"/>.</summary>
public sealed class SceneRenderer : IDisposable
{
    private static readonly Vector4 ClearColor = new(0.19f, 0.2f, 0.22f, 1f);

    private readonly IRenderer _renderer;
    private readonly Dictionary<PrimitiveMesh, IMesh> _primitives = new();

    public SceneRenderer(IRenderer renderer)
    {
        _renderer = renderer;
        _primitives[PrimitiveMesh.Cube] = renderer.CreateMesh(Primitives.CreateCube());
    }

    public void Render(Scene scene, IRenderTarget target, in ViewParams view)
    {
        _renderer.BeginPass(target, view, ClearColor);

        foreach (Entity entity in scene.Entities)
        {
            if (entity.GetComponent<MeshRenderer>() is not { } meshRenderer)
                continue;
            Matrix4x4 world = scene.GetWorldMatrix(entity);
            _renderer.DrawMesh(_primitives[meshRenderer.Mesh], world, meshRenderer.Color);
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

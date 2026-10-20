using System.Numerics;
using Menhir.Editor.Assets;
using Menhir.Editor.Lighting;
using Menhir.Editor.Logging;
using Menhir.Editor.Mathematics;
using Menhir.Editor.Projects;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Rendering;

/// <summary>What the viewport shows besides the scene's objects (Unity's scene view "Effects" menu).</summary>
public sealed class ViewportOptions
{
    public bool Skybox { get; set; } = true;
    public bool PostProcessing { get; set; } = true;
    public bool Grid { get; set; } = true;
    public bool Shadows { get; set; } = true;
}

/// <summary>Draws a <see cref="Scene"/> through an <see cref="IRenderer"/>.</summary>
public sealed class SceneRenderer : IDisposable
{
    // Background when the skybox is hidden (linear; the old flat gray).
    private static readonly Vector3 ClearColor = new(0.026f, 0.029f, 0.034f);
    private static readonly Vector4 SelectionColor = new(1f, 0.6f, 0.1f, 1f);
    private static readonly Bounds CubeBounds = new(new Vector3(-0.5f), new Vector3(0.5f));

    private readonly IRenderer _renderer;
    private readonly Func<Guid, string?> _resolveAssetPath;
    private readonly Dictionary<PrimitiveMesh, IMesh> _primitives = new();
    private readonly Dictionary<Guid, (ITexture? Texture, DateTime Modified)> _textures = new();

    /// <param name="resolveAssetPath">Absolute file path of an asset GUID (for textures), or null.</param>
    public SceneRenderer(IRenderer renderer, Func<Guid, string?> resolveAssetPath)
    {
        _renderer = renderer;
        _resolveAssetPath = resolveAssetPath;
        _primitives[PrimitiveMesh.Cube] = renderer.CreateMesh(Primitives.CreateCube());
    }

    /// <param name="settings">The project's pipeline settings (shadows).</param>
    /// <param name="selectedEntityId">Entity drawn with a selection outline, 0 for none.</param>
    public void Render(Scene scene, IRenderTarget target, in ViewParams view, ViewportOptions options,
        RenderingSettings settings, long selectedEntityId = 0)
    {
        SceneEnvironment environment = SceneEnvironment.Gather(scene);

        var draws = new List<MeshDraw>();
        var casters = new List<MeshDraw>();
        Bounds? casterBounds = null;
        foreach (Entity entity in scene.Entities)
        {
            if (entity.GetComponent<MeshRenderer>() is not { } meshRenderer)
                continue;
            Matrix4x4 world = scene.GetWorldMatrix(entity);
            var draw = new MeshDraw(_primitives[meshRenderer.Mesh], world,
                new SurfaceParams(meshRenderer.Color, meshRenderer.Metallic, meshRenderer.Smoothness, meshRenderer.ReceiveShadows));
            draws.Add(draw);
            if (!meshRenderer.CastShadows)
                continue;
            casters.Add(draw);
            Bounds bounds = Bounds.Transform(CubeBounds, world);
            casterBounds = casterBounds?.Encapsulate(bounds) ?? bounds;
        }

        if (options.Shadows)
        {
            Matrix4x4.Invert(view.View, out Matrix4x4 cameraToWorld);
            var camera = new CameraInfo(cameraToWorld, view.FieldOfViewY, target.Width / (float)target.Height, view.Near);
            _renderer.RenderShadows(environment,
                ShadowSetup.MainLight(environment, camera, settings, casterBounds),
                ShadowSetup.AdditionalLights(environment, settings, casterBounds),
                casters);
        }

        _renderer.BeginFrame(target, view, environment, ClearColor);
        if (options.Skybox)
            _renderer.DrawSkybox(environment.Sky);
        foreach (MeshDraw draw in draws)
            _renderer.DrawMesh(draw.Mesh, draw.Model, draw.Surface);

        if (options.Grid)
            _renderer.DrawGrid();

        PostProcessStack stack = options.PostProcessing ? environment.PostProcessing : PostProcessStack.Empty;
        ITexture? lookup = stack.ColorLookup?.Texture is { } guid ? GetTexture(guid) : null;
        _renderer.PostProcess(stack, lookup);

        if (scene.Find(selectedEntityId) is { } selected && selected.GetComponent<MeshRenderer>() is { } selectedRenderer)
            _renderer.DrawOutline(_primitives[selectedRenderer.Mesh], scene.GetWorldMatrix(selected), SelectionColor);
        _renderer.EndFrame();
    }

    /// <summary>A texture asset, loaded on first use and again when its file changes.</summary>
    private ITexture? GetTexture(Guid guid)
    {
        string? path = _resolveAssetPath(guid);
        DateTime modified = path != null && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (_textures.TryGetValue(guid, out var cached) && cached.Modified == modified)
            return cached.Texture;

        cached.Texture?.Dispose();
        ITexture? texture = null;
        if (path != null && modified != DateTime.MinValue)
        {
            try
            {
                texture = _renderer.CreateTexture(PngDecoder.Decode(File.ReadAllBytes(path)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                          or NotSupportedException)
            {
                Log.Warning($"Could not load the texture {Path.GetFileName(path)}: {e.Message}");
            }
        }
        _textures[guid] = (texture, modified);
        return texture;
    }

    public void Dispose()
    {
        foreach (IMesh mesh in _primitives.Values)
            mesh.Dispose();
        _primitives.Clear();
        foreach (var (texture, _) in _textures.Values)
            texture?.Dispose();
        _textures.Clear();
    }
}

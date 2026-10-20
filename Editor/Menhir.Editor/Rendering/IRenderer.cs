using System.Numerics;
using Menhir.Editor.Assets;
using Menhir.Editor.Lighting;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Rendering;

/// <summary>
/// The only way editor code talks to the GPU. Keeps raw graphics API calls inside
/// one backend (currently OpenGL 3.3, see <c>Rendering/OpenGL</c>).
/// </summary>
/// <remarks>
/// A frame goes: <see cref="BeginFrame"/>, then the scene (<see cref="DrawSkybox"/>,
/// <see cref="DrawMesh"/>, <see cref="DrawGrid"/>) into an HDR buffer, then <see cref="PostProcess"/>
/// into the displayed image, then the selection outline that post-processing does not touch,
/// then <see cref="EndFrame"/>. The grid is part of the scene so distortion effects move it with
/// the objects.
/// </remarks>
public interface IRenderer : IDisposable
{
    IRenderTarget CreateRenderTarget(int width, int height);

    IMesh CreateMesh(MeshData data);

    /// <summary>An 8-bit RGBA texture (e.g. a color lookup table).</summary>
    ITexture CreateTexture(ImageData image);

    /// <summary>Binds <paramref name="target"/>'s HDR buffer and clears it to <paramref name="clearColor"/> (linear).</summary>
    void BeginFrame(IRenderTarget target, in ViewParams view, SceneEnvironment environment, Vector3 clearColor);

    /// <summary>The procedural sky behind everything; draw it before the meshes.</summary>
    void DrawSkybox(ProceduralSkybox sky);

    void DrawMesh(IMesh mesh, in Matrix4x4 model, in SurfaceParams surface);

    /// <summary>
    /// Applies the effects of <paramref name="stack"/> to the HDR image and writes the displayed image
    /// (tonemapped, gamma-encoded). <paramref name="colorLookup"/> is the texture of the Color Lookup effect.
    /// </summary>
    void PostProcess(PostProcessStack stack, ITexture? colorLookup);

    /// <summary>Flat-colored silhouette around a mesh already drawn with <paramref name="model"/>.</summary>
    void DrawOutline(IMesh mesh, in Matrix4x4 model, Vector4 color);

    /// <summary>Editor grid on the y = 0 plane, fading with distance. Draw it after the meshes.</summary>
    void DrawGrid();

    /// <summary>Restores the state the renderer found (window framebuffer, viewport).</summary>
    void EndFrame();
}

/// <summary>An offscreen image: HDR color, depth and the final (displayed) color.</summary>
public interface IRenderTarget : IDisposable
{
    int Width { get; }
    int Height { get; }

    /// <summary>Backend texture handle of the displayed image, usable as an ImGui texture ID.</summary>
    nint ColorTexture { get; }

    void Resize(int width, int height);
}

public interface IMesh : IDisposable
{
}

public interface ITexture : IDisposable
{
    int Width { get; }
    int Height { get; }
}

/// <summary>Material values of a mesh, like URP's Lit (linear base color).</summary>
public readonly record struct SurfaceParams(Vector4 BaseColor, float Metallic, float Smoothness);

/// <summary>Camera data for a frame.</summary>
public readonly record struct ViewParams(
    Matrix4x4 View,
    Matrix4x4 Projection,
    Vector3 CameraPosition,
    float FieldOfViewY,
    float Near,
    float Far);

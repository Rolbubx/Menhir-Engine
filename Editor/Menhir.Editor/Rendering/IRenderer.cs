using System.Numerics;

namespace Menhir.Editor.Rendering;

/// <summary>
/// The only way editor code talks to the GPU. Keeps raw graphics API calls inside
/// one backend (currently OpenGL 3.3, see <c>Rendering/OpenGL</c>).
/// </summary>
public interface IRenderer : IDisposable
{
    IRenderTarget CreateRenderTarget(int width, int height);

    IMesh CreateMesh(MeshData data);

    /// <summary>Binds and clears <paramref name="target"/>; draws until <see cref="EndPass"/> go into it.</summary>
    void BeginPass(IRenderTarget target, in ViewParams view, Vector4 clearColor);

    void DrawMesh(IMesh mesh, in Matrix4x4 model, Vector4 color);

    /// <summary>Editor grid on the y = 0 plane, fading with distance. Draw it after opaque meshes.</summary>
    void DrawGrid();

    /// <summary>Restores the state the renderer found (window framebuffer, viewport).</summary>
    void EndPass();
}

/// <summary>An offscreen color + depth buffer.</summary>
public interface IRenderTarget : IDisposable
{
    int Width { get; }
    int Height { get; }

    /// <summary>Backend texture handle of the color buffer, usable as an ImGui texture ID.</summary>
    nint ColorTexture { get; }

    void Resize(int width, int height);
}

public interface IMesh : IDisposable
{
}

/// <summary>Camera data for a render pass.</summary>
public readonly record struct ViewParams(Matrix4x4 View, Matrix4x4 Projection, Vector3 CameraPosition);

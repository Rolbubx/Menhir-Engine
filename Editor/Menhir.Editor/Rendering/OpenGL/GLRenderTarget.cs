using System.Numerics;
using Silk.NET.OpenGL;

namespace Menhir.Editor.Rendering.OpenGL;

/// <summary>
/// A viewport's buffers: the scene in HDR (RGBA16F) with a depth texture, and the displayed
/// image (RGBA8) that post-processing writes and editor overlays draw on, sharing that depth.
/// </summary>
internal sealed unsafe class GLRenderTarget : IRenderTarget
{
    private readonly GL _gl;

    public uint SceneFramebuffer { get; private set; }
    public uint OutputFramebuffer { get; private set; }
    public uint HdrTexture { get; private set; }
    public uint DepthTexture { get; private set; }
    public uint OutputTexture { get; private set; }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public nint ColorTexture => (nint)OutputTexture;

    /// <summary>View-projection of the previous frame, for motion blur; null after a resize.</summary>
    public Matrix4x4? PreviousViewProjection { get; set; }

    public GLRenderTarget(GL gl, int width, int height)
    {
        _gl = gl;
        Create(width, height);
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height)
            return;
        Delete();
        Create(width, height);
    }

    private void Create(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        PreviousViewProjection = null;

        HdrTexture = GLTextures.Create2D(_gl, Width, Height, InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.HalfFloat);
        OutputTexture = GLTextures.Create2D(_gl, Width, Height, InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte);
        DepthTexture = GLTextures.Create2D(_gl, Width, Height, InternalFormat.Depth24Stencil8, PixelFormat.DepthStencil,
            PixelType.UnsignedInt248, TextureMinFilter.Nearest);

        SceneFramebuffer = CreateFramebuffer(HdrTexture);
        OutputFramebuffer = CreateFramebuffer(OutputTexture);
    }

    private uint CreateFramebuffer(uint color)
    {
        _gl.GetInteger(GetPName.DrawFramebufferBinding, out int previous);
        uint framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, color, 0);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
            TextureTarget.Texture2D, DepthTexture, 0);
        GLEnum status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previous);
        if (status != GLEnum.FramebufferComplete)
            throw new InvalidOperationException($"Framebuffer incomplete: {status}");
        return framebuffer;
    }

    private void Delete()
    {
        _gl.DeleteFramebuffer(SceneFramebuffer);
        _gl.DeleteFramebuffer(OutputFramebuffer);
        _gl.DeleteTexture(HdrTexture);
        _gl.DeleteTexture(OutputTexture);
        _gl.DeleteTexture(DepthTexture);
    }

    public void Dispose() => Delete();
}

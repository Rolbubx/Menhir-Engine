using Silk.NET.OpenGL;

namespace Menhir.Editor.Rendering.OpenGL;

/// <summary>Framebuffer with an RGBA8 color texture and a depth-stencil renderbuffer.</summary>
internal sealed unsafe class GLRenderTarget : IRenderTarget
{
    private readonly GL _gl;
    private uint _colorTexture;
    private uint _depthBuffer;

    public uint Framebuffer { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public nint ColorTexture => (nint)_colorTexture;

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

        _colorTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _colorTexture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)Width, (uint)Height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.BindTexture(TextureTarget.Texture2D, 0);

        _depthBuffer = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthBuffer);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, (uint)Width, (uint)Height);
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

        _gl.GetInteger(GetPName.DrawFramebufferBinding, out int previous);
        Framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _colorTexture, 0);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer, _depthBuffer);
        GLEnum status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previous);

        if (status != GLEnum.FramebufferComplete)
            throw new InvalidOperationException($"Framebuffer incomplete: {status}");
    }

    private void Delete()
    {
        _gl.DeleteFramebuffer(Framebuffer);
        _gl.DeleteTexture(_colorTexture);
        _gl.DeleteRenderbuffer(_depthBuffer);
    }

    public void Dispose() => Delete();
}

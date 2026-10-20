using Menhir.Editor.Assets;
using Silk.NET.OpenGL;

namespace Menhir.Editor.Rendering.OpenGL;

internal static unsafe class GLTextures
{
    /// <summary>An empty (or filled from <paramref name="pixels"/>) 2D texture, clamped, linear filtering.</summary>
    public static uint Create2D(GL gl, int width, int height, InternalFormat format, PixelFormat pixelFormat,
        PixelType type, TextureMinFilter filter = TextureMinFilter.Linear, void* pixels = null,
        TextureWrapMode wrap = TextureWrapMode.ClampToEdge)
    {
        uint texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        gl.TexImage2D(TextureTarget.Texture2D, 0, format, (uint)width, (uint)height, 0, pixelFormat, type, pixels);
        var mag = filter == TextureMinFilter.Nearest ? TextureMagFilter.Nearest : TextureMagFilter.Linear;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)filter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)mag);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)wrap);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)wrap);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        return texture;
    }
}

/// <summary>An RGBA8 texture from an image.</summary>
internal sealed unsafe class GLTexture : ITexture
{
    private readonly GL _gl;

    public uint Handle { get; }
    public int Width { get; }
    public int Height { get; }

    public GLTexture(GL gl, ImageData image)
    {
        _gl = gl;
        Width = image.Width;
        Height = image.Height;
        fixed (byte* pixels = image.Rgba)
            Handle = GLTextures.Create2D(gl, Width, Height, InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte,
                pixels: pixels);
    }

    public void Dispose() => _gl.DeleteTexture(Handle);
}

/// <summary>
/// Temporary render textures for post-processing passes, kept between frames so a steady
/// viewport size allocates nothing. Everything taken is given back with <see cref="ReleaseAll"/>.
/// </summary>
internal sealed unsafe class GLRenderTexturePool : IDisposable
{
    internal sealed class Entry
    {
        public required uint Texture { get; init; }
        public required uint Framebuffer { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required InternalFormat Format { get; init; }
        public bool InUse { get; set; }
        public int LastUsedFrame { get; set; }
    }

    private readonly GL _gl;
    private readonly List<Entry> _entries = new();
    private int _frame;

    public GLRenderTexturePool(GL gl)
    {
        _gl = gl;
    }

    public Entry Get(int width, int height, InternalFormat format = InternalFormat.Rgba16f)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        Entry? entry = _entries.FirstOrDefault(e => !e.InUse && e.Width == width && e.Height == height && e.Format == format);
        if (entry == null)
        {
            (PixelFormat pixelFormat, PixelType type) = format == InternalFormat.R16f
                ? (PixelFormat.Red, PixelType.HalfFloat)
                : (PixelFormat.Rgba, PixelType.HalfFloat);
            uint texture = GLTextures.Create2D(_gl, width, height, format, pixelFormat, type);
            uint framebuffer = _gl.GenFramebuffer();
            _gl.GetInteger(GetPName.DrawFramebufferBinding, out int previous);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, texture, 0);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previous);
            entry = new Entry { Texture = texture, Framebuffer = framebuffer, Width = width, Height = height, Format = format };
            _entries.Add(entry);
        }
        entry.InUse = true;
        entry.LastUsedFrame = _frame;
        return entry;
    }

    /// <summary>Frees textures unused for a while (e.g. after the viewport was resized).</summary>
    public void ReleaseAll()
    {
        _frame++;
        foreach (Entry entry in _entries)
            entry.InUse = false;
        foreach (Entry stale in _entries.Where(e => _frame - e.LastUsedFrame > 120).ToList())
        {
            Delete(stale);
            _entries.Remove(stale);
        }
    }

    private void Delete(Entry entry)
    {
        _gl.DeleteFramebuffer(entry.Framebuffer);
        _gl.DeleteTexture(entry.Texture);
    }

    public void Dispose()
    {
        foreach (Entry entry in _entries)
            Delete(entry);
        _entries.Clear();
    }
}

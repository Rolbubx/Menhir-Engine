using System.Numerics;
using Menhir.Editor.Lighting;
using Silk.NET.OpenGL;

namespace Menhir.Editor.Rendering.OpenGL;

/// <summary>
/// The shadow map atlases (one for the sun's cascades, one for point and spot lights) and the
/// caster pass that fills them: depth only, with URP's depth and normal biases applied to casters.
/// </summary>
internal sealed unsafe class GLShadowMaps : IDisposable
{
    private readonly GL _gl;
    private readonly GLShaderProgram _caster;

    public Atlas Main { get; }
    public Atlas Additional { get; }

    public GLShadowMaps(GL gl)
    {
        _gl = gl;
        _caster = new GLShaderProgram(gl, ShadowShaders.CasterVertex, ShadowShaders.CasterFragment);
        Main = new Atlas(gl);
        Additional = new Atlas(gl);
    }

    public void Render(SceneEnvironment environment, MainLightShadows? main, AdditionalLightShadows? additional,
        IReadOnlyList<MeshDraw> casters)
    {
        _gl.GetInteger(GetPName.DrawFramebufferBinding, out int previousFramebuffer);
        Span<int> previousViewport = stackalloc int[4];
        _gl.GetInteger(GetPName.Viewport, previousViewport);

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.FrontFace(FrontFaceDirection.Ccw);
        _caster.Use();

        if (main != null)
        {
            Main.Begin(main.AtlasSize);
            _caster.Set("uPunctual", 0);
            _caster.Set("uLightDirection", Vector3.Normalize(environment.ToSun));
            foreach (ShadowSlice cascade in main.Cascades)
                DrawSlice(cascade, casters);
        }

        if (additional != null)
        {
            Additional.Begin(additional.AtlasSize);
            _caster.Set("uPunctual", 1);
            for (int light = 0; light < additional.FirstSlice.Length; light++)
            {
                int first = additional.FirstSlice[light];
                if (first < 0)
                    continue;
                _caster.Set("uLightPosition", environment.AdditionalLights[light].Position);
                int count = environment.AdditionalLights[light].IsSpot ? 1 : 6;
                for (int i = first; i < first + count; i++)
                    DrawSlice(additional.Slices[i], casters);
            }
        }

        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previousFramebuffer);
        _gl.Viewport(previousViewport[0], previousViewport[1], (uint)previousViewport[2], (uint)previousViewport[3]);
    }

    private void DrawSlice(ShadowSlice slice, IReadOnlyList<MeshDraw> casters)
    {
        (int x, int y, int size) = slice.AtlasPixels;
        _gl.Viewport(x, y, (uint)size, (uint)size);
        _caster.Set("uLightViewProjection", slice.ViewProjection);
        _caster.Set("uDepthBias", slice.DepthBias);
        _caster.Set("uNormalBias", slice.NormalBias);
        foreach (MeshDraw draw in casters)
        {
            Matrix4x4 normalMatrix = Matrix4x4.Invert(draw.Model, out Matrix4x4 inverse) ? Matrix4x4.Transpose(inverse) : draw.Model;
            _caster.Set("uModel", draw.Model);
            _caster.Set("uNormalMatrix", normalMatrix);
            ((GLMesh)draw.Mesh).Draw();
        }
    }

    public void Dispose()
    {
        _caster.Dispose();
        Main.Dispose();
        Additional.Dispose();
    }

    /// <summary>A square depth texture set up for hardware shadow comparison (sampler2DShadow).</summary>
    internal sealed class Atlas : IDisposable
    {
        private readonly GL _gl;
        private uint _framebuffer;

        public uint Texture { get; private set; }
        public int Size { get; private set; }

        public Atlas(GL gl)
        {
            _gl = gl;
        }

        /// <summary>Binds the atlas (resized if needed) and clears it to the far plane.</summary>
        public void Begin(int size)
        {
            if (size != Size)
                Create(size);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            _gl.Disable(EnableCap.ScissorTest);
            _gl.Viewport(0, 0, (uint)Size, (uint)Size);
            _gl.Clear(ClearBufferMask.DepthBufferBit);
        }

        private void Create(int size)
        {
            Delete();
            Size = size;
            Texture = GLTextures.Create2D(_gl, size, size, InternalFormat.DepthComponent24, PixelFormat.DepthComponent,
                PixelType.UnsignedInt);
            _gl.BindTexture(TextureTarget.Texture2D, Texture);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareFunc, (int)GLEnum.Lequal);
            _gl.BindTexture(TextureTarget.Texture2D, 0);

            _gl.GetInteger(GetPName.DrawFramebufferBinding, out int previous);
            _framebuffer = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                TextureTarget.Texture2D, Texture, 0);
            _gl.DrawBuffer(DrawBufferMode.None);
            _gl.ReadBuffer(ReadBufferMode.None);
            GLEnum status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previous);
            if (status != GLEnum.FramebufferComplete)
                throw new InvalidOperationException($"Shadow framebuffer incomplete: {status}");
        }

        private void Delete()
        {
            if (Texture == 0)
                return;
            _gl.DeleteFramebuffer(_framebuffer);
            _gl.DeleteTexture(Texture);
            Texture = 0;
        }

        public void Dispose() => Delete();
    }
}

using System.Numerics;
using Menhir.Editor.Lighting;
using Menhir.Editor.Mathematics;
using Menhir.Editor.PostProcessing;
using Silk.NET.OpenGL;
using Entry = Menhir.Editor.Rendering.OpenGL.GLRenderTexturePool.Entry;

namespace Menhir.Editor.Rendering.OpenGL;

/// <summary>
/// Runs the post-processing stack on a target's HDR image, in URP's order: depth of field,
/// motion blur, Panini projection, bloom, then the uber pass that writes the displayed image.
/// </summary>
internal sealed unsafe class GLPostProcessor : IDisposable
{
    private const int CurveSamples = PostProcessUniforms.CurveSamples;

    private readonly GL _gl;
    private readonly GLRenderTexturePool _pool;
    private readonly uint _emptyVao;

    private readonly GLShaderProgram _cocGaussian;
    private readonly GLShaderProgram _cocBokeh;
    private readonly GLShaderProgram _dofPrefilter;
    private readonly GLShaderProgram _dofGaussianBlur;
    private readonly GLShaderProgram _dofGaussianComposite;
    private readonly GLShaderProgram _dofBokeh;
    private readonly GLShaderProgram _dofPostFilter;
    private readonly GLShaderProgram _dofBokehComposite;
    private readonly GLShaderProgram _motionBlur;
    private readonly GLShaderProgram _panini;
    private readonly GLShaderProgram _bloomPrefilter;
    private readonly GLShaderProgram _bloomBlurH;
    private readonly GLShaderProgram _bloomBlurV;
    private readonly GLShaderProgram _bloomUpsample;
    private readonly GLShaderProgram _uber;

    private readonly uint _curveTexture;
    private readonly uint _whiteTexture;
    private readonly Dictionary<FilmGrainType, uint> _grainTextures = new();
    private readonly Random _random = new();

    public GLPostProcessor(GL gl)
    {
        _gl = gl;
        _pool = new GLRenderTexturePool(gl);
        _emptyVao = gl.GenVertexArray();

        GLShaderProgram Program(string fragment) => new(gl, PostProcessShaders.FullscreenVertex, fragment);
        _cocGaussian = Program(PostProcessShaders.CocGaussian);
        _cocBokeh = Program(PostProcessShaders.CocBokeh);
        _dofPrefilter = Program(PostProcessShaders.DofPrefilter);
        _dofGaussianBlur = Program(PostProcessShaders.DofGaussianBlur);
        _dofGaussianComposite = Program(PostProcessShaders.DofGaussianComposite);
        _dofBokeh = Program(PostProcessShaders.DofBokeh);
        _dofPostFilter = Program(PostProcessShaders.DofPostFilter);
        _dofBokehComposite = Program(PostProcessShaders.DofBokehComposite);
        _motionBlur = Program(PostProcessShaders.MotionBlur);
        _panini = Program(PostProcessShaders.Panini);
        _bloomPrefilter = Program(PostProcessShaders.BloomPrefilter);
        _bloomBlurH = Program(PostProcessShaders.BloomBlurH);
        _bloomBlurV = Program(PostProcessShaders.BloomBlurV);
        _bloomUpsample = Program(PostProcessShaders.BloomUpsample);
        _uber = Program(PostProcessShaders.Uber);

        _curveTexture = GLTextures.Create2D(gl, CurveSamples, 8, InternalFormat.R16f, PixelFormat.Red, PixelType.Float,
            wrap: TextureWrapMode.Repeat);
        byte[] white = [255, 255, 255, 255];
        fixed (byte* pixels = white)
            _whiteTexture = GLTextures.Create2D(gl, 1, 1, InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte, pixels: pixels);
    }

    /// <summary>Post-processes <paramref name="target"/>'s HDR image into its displayed image.</summary>
    public void Run(GLRenderTarget target, in ViewParams view, PostProcessStack stack, GLTexture? colorLookup)
    {
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);
        _gl.DepthMask(false);
        _gl.BindVertexArray(_emptyVao);

        int width = target.Width, height = target.Height;
        Matrix4x4 viewProjection = view.View * view.Projection;
        uint source = target.HdrTexture;

        if (stack.DepthOfField is { Mode: not DepthOfFieldMode.Off } dof)
            source = DepthOfField(target, view, source, dof);

        if (stack.MotionBlur is { Intensity: > 0f } motionBlur && target.PreviousViewProjection is { } previous)
            source = MotionBlur(target, viewProjection, previous, source, motionBlur);

        if (stack.PaniniProjection is { Distance: > 0f } panini)
            source = Panini(width, height, view, source, panini);

        Entry? bloom = stack.Bloom is { Intensity: > 0f } bloomSettings ? Bloom(width, height, source, bloomSettings) : null;

        Uber(target, source, bloom, stack, colorLookup);

        target.PreviousViewProjection = viewProjection;
        _gl.DepthMask(true);
        _pool.ReleaseAll();
    }

    private void Draw(Entry destination) => Draw(destination.Framebuffer, destination.Width, destination.Height);

    private void Draw(uint framebuffer, int width, int height)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    private static Vector2 Texel(int width, int height) => new(1f / width, 1f / height);

    // ---- Depth of field ---------------------------------------------------------------------

    private uint DepthOfField(GLRenderTarget target, in ViewParams view, uint source, DepthOfField dof)
    {
        int width = target.Width, height = target.Height;
        int halfWidth = Math.Max(1, width / 2), halfHeight = Math.Max(1, height / 2);
        Vector2 halfTexel = Texel(halfWidth, halfHeight);
        bool bokeh = dof.Mode == DepthOfFieldMode.Bokeh;
        float maxCoc = PostProcessMath.MaxBokehRadius(height);

        // Circle of confusion, full resolution.
        Entry coc = _pool.Get(width, height, InternalFormat.R16f);
        GLShaderProgram cocShader = bokeh ? _cocBokeh : _cocGaussian;
        cocShader.Use();
        cocShader.SetTexture("uDepth", 0, target.DepthTexture);
        cocShader.Set("uNear", view.Near);
        cocShader.Set("uFar", view.Far);
        if (bokeh)
        {
            cocShader.Set("uFocusDistance", dof.FocusDistance);
            cocShader.Set("uLensCoefficient", PostProcessMath.LensCoefficient(dof.FocalLength, dof.Aperture, dof.FocusDistance));
            cocShader.Set("uMaxCoc", maxCoc);
        }
        else
        {
            cocShader.Set("uStart", dof.GaussianStart);
            cocShader.Set("uEnd", MathF.Max(dof.GaussianEnd, dof.GaussianStart + 0.01f));
        }
        Draw(coc);

        // Half resolution color with the CoC in alpha.
        Entry prefiltered = _pool.Get(halfWidth, halfHeight);
        _dofPrefilter.Use();
        _dofPrefilter.SetTexture("uSource", 0, source);
        _dofPrefilter.SetTexture("uCoc", 1, coc.Texture);
        _dofPrefilter.Set("uBokeh", bokeh ? 1 : 0);
        _dofPrefilter.Set("uMaxCoc", maxCoc);
        _dofPrefilter.Set("uTexelSize", halfTexel);
        Draw(prefiltered);

        Entry result = _pool.Get(width, height);
        if (!bokeh)
        {
            // Wider steps for a larger radius; high quality takes smaller steps (and twice the passes).
            int passes = dof.HighQualitySampling ? 2 : 1;
            float step = dof.GaussianMaxRadius * (dof.HighQualitySampling ? 1.5f : 2.5f);
            Entry blurred = prefiltered;
            for (int i = 0; i < passes; i++)
            {
                Entry horizontal = _pool.Get(halfWidth, halfHeight);
                _dofGaussianBlur.Use();
                _dofGaussianBlur.SetTexture("uSource", 0, blurred.Texture);
                _dofGaussianBlur.Set("uDirection", new Vector2(halfTexel.X * step, 0));
                Draw(horizontal);

                Entry vertical = _pool.Get(halfWidth, halfHeight);
                _dofGaussianBlur.SetTexture("uSource", 0, horizontal.Texture);
                _dofGaussianBlur.Set("uDirection", new Vector2(0, halfTexel.Y * step));
                Draw(vertical);
                blurred = vertical;
            }

            _dofGaussianComposite.Use();
            _dofGaussianComposite.SetTexture("uSource", 0, source);
            _dofGaussianComposite.SetTexture("uBlurred", 1, blurred.Texture);
            _dofGaussianComposite.SetTexture("uCoc", 2, coc.Texture);
            Draw(result);
            return result.Texture;
        }

        Entry gathered = _pool.Get(halfWidth, halfHeight);
        _dofBokeh.Use();
        _dofBokeh.SetTexture("uSource", 0, prefiltered.Texture);
        _dofBokeh.Set("uKernel", PostProcessMath.BokehKernel(dof.BladeCount, dof.BladeCurvature, dof.BladeRotation));
        _dofBokeh.Set("uMaxCoc", maxCoc);
        _dofBokeh.Set("uRcpAspect", height / (float)width);
        _dofBokeh.Set("uTexelSize", halfTexel);
        Draw(gathered);

        Entry smoothed = _pool.Get(halfWidth, halfHeight);
        _dofPostFilter.Use();
        _dofPostFilter.SetTexture("uSource", 0, gathered.Texture);
        _dofPostFilter.Set("uTexelSize", halfTexel);
        Draw(smoothed);

        _dofBokehComposite.Use();
        _dofBokehComposite.SetTexture("uSource", 0, source);
        _dofBokehComposite.SetTexture("uBlurred", 1, smoothed.Texture);
        _dofBokehComposite.SetTexture("uCoc", 2, coc.Texture);
        _dofBokehComposite.Set("uMaxCoc", maxCoc);
        _dofBokehComposite.Set("uTexelSize", halfTexel);
        Draw(result);
        return result.Texture;
    }

    // ---- Motion blur, Panini ----------------------------------------------------------------

    private uint MotionBlur(GLRenderTarget target, Matrix4x4 viewProjection, Matrix4x4 previous, uint source, MotionBlur settings)
    {
        Matrix4x4.Invert(viewProjection, out Matrix4x4 inverse);
        Entry result = _pool.Get(target.Width, target.Height);
        _motionBlur.Use();
        _motionBlur.SetTexture("uSource", 0, source);
        _motionBlur.SetTexture("uDepth", 1, target.DepthTexture);
        _motionBlur.Set("uInverseViewProjection", inverse);
        _motionBlur.Set("uPreviousViewProjection", previous);
        _motionBlur.Set("uIntensity", settings.Intensity);
        _motionBlur.Set("uClamp", settings.Clamp);
        _motionBlur.Set("uSamples", PostProcessMath.MotionBlurSamples(settings.Quality));
        Draw(result);
        return result.Texture;
    }

    private uint Panini(int width, int height, in ViewParams view, uint source, PaniniProjection settings)
    {
        Entry result = _pool.Get(width, height);
        _panini.Use();
        _panini.SetTexture("uSource", 0, source);
        _panini.Set("uParams", PostProcessMath.Panini(settings, view.FieldOfViewY, width / (float)height));
        Draw(result);
        return result.Texture;
    }

    // ---- Bloom ------------------------------------------------------------------------------

    private Entry Bloom(int width, int height, uint source, Bloom settings)
    {
        int downscale = settings.Downscale == BloomDownscale.Half ? 2 : 4;
        int mipWidth = Math.Max(1, width / downscale), mipHeight = Math.Max(1, height / downscale);

        // As many mips as the size allows, at most MaxIterations.
        int maxSize = Math.Max(mipWidth, mipHeight);
        int iterations = Math.Clamp((int)MathF.Floor(MathF.Log2(maxSize)) - 1, 1, Math.Clamp(settings.MaxIterations, 1, 16));

        float threshold = MathF.Pow(MathF.Max(settings.Threshold, 0f), 2.2f);
        Entry[] mips = new Entry[iterations];
        mips[0] = _pool.Get(mipWidth, mipHeight);
        _bloomPrefilter.Use();
        _bloomPrefilter.SetTexture("uSource", 0, source);
        _bloomPrefilter.Set("uSourceTexelSize", Texel(width, height));
        _bloomPrefilter.Set("uThreshold", threshold);
        _bloomPrefilter.Set("uKnee", threshold * 0.5f);
        _bloomPrefilter.Set("uClampMax", settings.Clamp);
        Draw(mips[0]);

        // Downsample: horizontal blur into the half size, then vertical blur.
        for (int i = 1; i < iterations; i++)
        {
            Entry previous = mips[i - 1];
            int w = Math.Max(1, previous.Width / 2), h = Math.Max(1, previous.Height / 2);
            Entry horizontal = _pool.Get(w, h);
            _bloomBlurH.Use();
            _bloomBlurH.SetTexture("uSource", 0, previous.Texture);
            _bloomBlurH.Set("uSourceTexelSize", Texel(previous.Width, previous.Height));
            Draw(horizontal);

            mips[i] = _pool.Get(w, h);
            _bloomBlurV.Use();
            _bloomBlurV.SetTexture("uSource", 0, horizontal.Texture);
            _bloomBlurV.Set("uSourceTexelSize", Texel(w, h));
            Draw(mips[i]);
        }

        // Upsample: blend each mip with the (already combined) smaller one.
        float scatter = 0.05f + (0.95f - 0.05f) * Math.Clamp(settings.Scatter, 0f, 1f);
        Entry lower = mips[iterations - 1];
        for (int i = iterations - 2; i >= 0; i--)
        {
            Entry combined = _pool.Get(mips[i].Width, mips[i].Height);
            _bloomUpsample.Use();
            _bloomUpsample.SetTexture("uSource", 0, mips[i].Texture);
            _bloomUpsample.SetTexture("uLowMip", 1, lower.Texture);
            _bloomUpsample.Set("uLowMipTexelSize", Texel(lower.Width, lower.Height));
            _bloomUpsample.Set("uScatter", scatter);
            _bloomUpsample.Set("uHighQuality", settings.HighQualityFiltering ? 1 : 0);
            Draw(combined);
            lower = combined;
        }
        return lower;
    }

    // ---- Uber -------------------------------------------------------------------------------

    private void Uber(GLRenderTarget target, uint source, Entry? bloom, PostProcessStack stack, GLTexture? colorLookup)
    {
        GLShaderProgram u = _uber;
        u.Use();
        u.SetTexture("uSource", 0, source);
        u.Set("uScreenSize", new Vector2(target.Width, target.Height));

        u.Set("uBloomOn", bloom != null ? 1 : 0);
        u.SetTexture("uBloom", 1, bloom?.Texture ?? _whiteTexture);
        if (bloom != null && stack.Bloom is { } b)
        {
            u.Set("uBloomHighQuality", b.HighQualityFiltering ? 1 : 0);
            u.Set("uBloomTexelSize", Texel(bloom.Width, bloom.Height));
            u.Set("uBloomIntensity", b.Intensity);
            u.Set("uBloomTint", PostProcessMath.BloomTint(b.Tint));
        }

        foreach (ShaderUniform uniform in PostProcessUniforms.Uber(stack))
            u.Set(uniform);
        if (stack.ColorCurves is { } curves)
            UploadCurves(curves);
        u.SetTexture("uCurves", 2, _curveTexture);

        Vignette? vignette = stack.Vignette is { Intensity: > 0f } v ? v : null;
        u.Set("uVignetteOn", vignette != null ? 1 : 0);
        if (vignette != null)
        {
            u.Set("uVignetteColor", Xyz(vignette.Color));
            u.Set("uVignetteCenter", vignette.Center);
            u.Set("uVignetteParams", new Vector3(vignette.Intensity * 3f, vignette.Smoothness * 5f,
                vignette.Rounded ? target.Width / (float)target.Height : 1f));
        }

        bool lut = stack.ColorLookup is { Contribution: > 0f } && colorLookup != null &&
                   colorLookup.Width == colorLookup.Height * colorLookup.Height;
        u.Set("uLutOn", lut ? 1 : 0);
        u.SetTexture("uLut", 3, lut ? colorLookup!.Handle : _whiteTexture);
        if (lut)
            u.Set("uLutParams", new Vector4(1f / colorLookup!.Width, 1f / colorLookup.Height, colorLookup.Height - 1,
                stack.ColorLookup!.Contribution));

        FilmGrain? grain = stack.FilmGrain is { Intensity: > 0f } g ? g : null;
        u.Set("uGrainOn", grain != null ? 1 : 0);
        u.SetTexture("uGrain", 4, grain != null ? GrainTexture(grain.GrainType) : _whiteTexture);
        if (grain != null)
        {
            u.Set("uGrainParams", new Vector2(grain.Intensity * 4f, grain.Response));
            u.Set("uGrainScaleOffset", new Vector4(target.Width / (float)FilmGrainNoise.Size,
                target.Height / (float)FilmGrainNoise.Size, (float)_random.NextDouble(), (float)_random.NextDouble()));
        }
        u.Set("uDitherSeed", (float)_random.NextDouble() * 100f);

        Draw(target.OutputFramebuffer, target.Width, target.Height);
    }

    private void UploadCurves(ColorCurves curves)
    {
        float[] values = PostProcessUniforms.BakeCurves(curves);
        _gl.BindTexture(TextureTarget.Texture2D, _curveTexture);
        fixed (float* data = values)
            _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, CurveSamples, 8, PixelFormat.Red, PixelType.Float, data);
    }

    private uint GrainTexture(FilmGrainType type)
    {
        if (!_grainTextures.TryGetValue(type, out uint texture))
        {
            byte[] noise = FilmGrainNoise.Generate(type);
            fixed (byte* pixels = noise)
                texture = GLTextures.Create2D(_gl, FilmGrainNoise.Size, FilmGrainNoise.Size, InternalFormat.R8, PixelFormat.Red,
                    PixelType.UnsignedByte, pixels: pixels, wrap: TextureWrapMode.Repeat);
            _grainTextures[type] = texture;
        }
        return texture;
    }

    private static Vector3 Xyz(Vector4 v) => new(v.X, v.Y, v.Z);

    public void Dispose()
    {
        foreach (GLShaderProgram program in new[]
                 {
                     _cocGaussian, _cocBokeh, _dofPrefilter, _dofGaussianBlur, _dofGaussianComposite, _dofBokeh,
                     _dofPostFilter, _dofBokehComposite, _motionBlur, _panini, _bloomPrefilter, _bloomBlurH, _bloomBlurV,
                     _bloomUpsample, _uber,
                 })
            program.Dispose();
        _pool.Dispose();
        _gl.DeleteTexture(_curveTexture);
        _gl.DeleteTexture(_whiteTexture);
        foreach (uint texture in _grainTextures.Values)
            _gl.DeleteTexture(texture);
        _gl.DeleteVertexArray(_emptyVao);
    }
}

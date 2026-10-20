using System.Numerics;
using Menhir.Editor.Assets;
using Menhir.Editor.Lighting;
using Menhir.Editor.Mathematics;
using Menhir.Editor.Scenes;
using Silk.NET.OpenGL;

namespace Menhir.Editor.Rendering.OpenGL;

/// <summary>OpenGL 3.3 Core implementation of <see cref="IRenderer"/>.</summary>
public sealed unsafe class GLRenderer : IRenderer
{
    private const float OutlineScale = 1.06f;

    private enum Phase
    {
        None,
        Scene,
        Overlays,
    }

    private readonly GL _gl;
    private readonly GLShaderProgram _meshShader;
    private readonly GLShaderProgram _unlitShader;
    private readonly GLShaderProgram _gridShader;
    private readonly GLShaderProgram _skyShader;
    private readonly GLPostProcessor _postProcessor;
    private readonly uint _gridVao;
    private readonly uint _gridVbo;
    private readonly uint _emptyVao;

    private readonly Vector4[] _lightPositionRange = new Vector4[SceneEnvironment.MaxAdditionalLights];
    private readonly Vector4[] _lightColor = new Vector4[SceneEnvironment.MaxAdditionalLights];
    private readonly Vector4[] _lightDirectionCos = new Vector4[SceneEnvironment.MaxAdditionalLights];
    private readonly float[] _lightCosInner = new float[SceneEnvironment.MaxAdditionalLights];

    private Phase _phase;
    private GLRenderTarget? _target;
    private SceneEnvironment? _environment;
    private ViewParams _view;
    private Matrix4x4 _viewProjection;
    private int _previousFramebuffer;
    private readonly int[] _previousViewport = new int[4];

    public GLRenderer(GL gl)
    {
        _gl = gl;
        _meshShader = new GLShaderProgram(gl, Shaders.MeshVertex, Shaders.MeshFragment);
        _unlitShader = new GLShaderProgram(gl, Shaders.MeshVertex, Shaders.UnlitFragment);
        _gridShader = new GLShaderProgram(gl, Shaders.GridVertex, Shaders.GridFragment);
        _skyShader = new GLShaderProgram(gl, SkyShaders.Vertex, SkyShaders.Fragment);
        _postProcessor = new GLPostProcessor(gl);

        float[] corners = [-1, -1, 1, -1, 1, 1, -1, -1, 1, 1, -1, 1];
        _gridVao = gl.GenVertexArray();
        gl.BindVertexArray(_gridVao);
        _gridVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _gridVbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, corners, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
        gl.BindVertexArray(0);
        _emptyVao = gl.GenVertexArray();
    }

    public IRenderTarget CreateRenderTarget(int width, int height) => new GLRenderTarget(_gl, width, height);

    public IMesh CreateMesh(MeshData data) => new GLMesh(_gl, data);

    public ITexture CreateTexture(ImageData image) => new GLTexture(_gl, image);

    public void BeginFrame(IRenderTarget target, in ViewParams view, SceneEnvironment environment, Vector3 clearColor)
    {
        if (_phase != Phase.None)
            throw new InvalidOperationException("EndFrame was not called for the previous frame.");
        _phase = Phase.Scene;
        _target = (GLRenderTarget)target;
        _environment = environment;
        _view = view;
        _viewProjection = view.View * view.Projection;

        _gl.GetInteger(GetPName.DrawFramebufferBinding, out _previousFramebuffer);
        _gl.GetInteger(GetPName.Viewport, _previousViewport);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _target.SceneFramebuffer);
        _gl.Viewport(0, 0, (uint)target.Width, (uint)target.Height);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);

        _gl.ClearColor(clearColor.X, clearColor.Y, clearColor.Z, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        SetLighting(environment);
    }

    private void SetLighting(SceneEnvironment environment)
    {
        int count = Math.Min(environment.AdditionalLights.Count, SceneEnvironment.MaxAdditionalLights);
        for (int i = 0; i < count; i++)
        {
            PunctualLight light = environment.AdditionalLights[i];
            _lightPositionRange[i] = new Vector4(light.Position, light.Range);
            _lightColor[i] = new Vector4(light.Color, 1f);
            _lightDirectionCos[i] = new Vector4(light.Direction, light.SpotCosOuter);
            _lightCosInner[i] = light.SpotCosInner;
        }

        _meshShader.Use();
        _meshShader.Set("uToSun", environment.ToSun);
        _meshShader.Set("uSunColor", environment.SunColor);
        _meshShader.Set("uAmbientTop", environment.AmbientTop);
        _meshShader.Set("uAmbientHorizon", environment.AmbientHorizon);
        _meshShader.Set("uAmbientBottom", environment.AmbientBottom);
        _meshShader.Set("uLightCount", count);
        if (count > 0)
        {
            _meshShader.Set("uLightPositionRange", _lightPositionRange.AsSpan(0, count));
            _meshShader.Set("uLightColor", _lightColor.AsSpan(0, count));
            _meshShader.Set("uLightDirectionCos", _lightDirectionCos.AsSpan(0, count));
            _meshShader.Set("uLightCosInner", _lightCosInner.AsSpan(0, count));
        }
    }

    public void DrawSkybox(ProceduralSkybox sky)
    {
        EnsurePhase(Phase.Scene);
        SceneEnvironment environment = _environment!;

        // At the far plane, behind everything, without writing depth.
        Matrix4x4.Invert(_viewProjection, out Matrix4x4 inverse);
        _gl.Disable(EnableCap.CullFace);
        _gl.DepthMask(false);
        _gl.DepthFunc(DepthFunction.Lequal);

        _skyShader.Use();
        _skyShader.Set("uInverseViewProjection", inverse);
        _skyShader.Set("uToSun", environment.ToSun);
        _skyShader.Set("uSunColor", environment.SunColor);
        _skyShader.Set("uSunDisk", (int)sky.SunDisk);
        _skyShader.Set("uSunSize", sky.SunSize);
        _skyShader.Set("uSunSizeConvergence", sky.SunSizeConvergence);
        _skyShader.Set("uAtmosphereThickness", sky.AtmosphereThickness);
        Vector4 tint = ColorSpace.LinearToGamma(sky.SkyTint);
        _skyShader.Set("uSkyTintGamma", new Vector3(tint.X, tint.Y, tint.Z));
        _skyShader.Set("uGroundColor", new Vector3(sky.GroundColor.X, sky.GroundColor.Y, sky.GroundColor.Z));
        _skyShader.Set("uExposure", sky.Exposure);
        _gl.BindVertexArray(_emptyVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(true);
    }

    public void DrawMesh(IMesh mesh, in Matrix4x4 model, in SurfaceParams surface)
    {
        EnsurePhase(Phase.Scene);

        // Inverse-transpose keeps normals perpendicular under non-uniform scale.
        Matrix4x4 normalMatrix = Matrix4x4.Invert(model, out Matrix4x4 inverse)
            ? Matrix4x4.Transpose(inverse)
            : model;

        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.FrontFace(FrontFaceDirection.Ccw);

        _meshShader.Use();
        _meshShader.Set("uModel", model);
        _meshShader.Set("uNormalMatrix", normalMatrix);
        _meshShader.Set("uViewProjection", _viewProjection);
        _meshShader.Set("uCameraPosition", _view.CameraPosition);
        _meshShader.Set("uBaseColor", surface.BaseColor);
        _meshShader.Set("uMetallic", surface.Metallic);
        _meshShader.Set("uSmoothness", surface.Smoothness);
        ((GLMesh)mesh).Draw();
    }

    public void PostProcess(PostProcessStack stack, ITexture? colorLookup)
    {
        EnsurePhase(Phase.Scene);
        _postProcessor.Run(_target!, _view, stack, colorLookup as GLTexture);

        // Overlays draw on the displayed image, depth-tested against the scene.
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _target!.OutputFramebuffer);
        _gl.Viewport(0, 0, (uint)_target.Width, (uint)_target.Height);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _phase = Phase.Overlays;
    }

    public void DrawOutline(IMesh mesh, in Matrix4x4 model, Vector4 color)
    {
        EnsurePhase(Phase.Overlays);

        // Inverted hull: a slightly larger copy showing only its back faces,
        // which peek out around the original mesh.
        Matrix4x4 hull = Matrix4x4.CreateScale(OutlineScale) * model;

        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Front);
        _gl.FrontFace(FrontFaceDirection.Ccw);
        _gl.DepthMask(false);

        _unlitShader.Use();
        _unlitShader.Set("uModel", hull);
        _unlitShader.Set("uNormalMatrix", Matrix4x4.Identity);
        _unlitShader.Set("uViewProjection", _viewProjection);
        _unlitShader.Set("uBaseColor", color);
        ((GLMesh)mesh).Draw();

        _gl.CullFace(TriangleFace.Back);
        _gl.DepthMask(true);
    }

    public void DrawGrid()
    {
        EnsurePhase(Phase.Scene);

        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.DepthMask(false);

        // See further when the camera is high above the ground.
        float fadeDistance = 60f + MathF.Abs(_view.CameraPosition.Y) * 10f;

        _gridShader.Use();
        _gridShader.Set("uViewProjection", _viewProjection);
        _gridShader.Set("uCameraPosition", _view.CameraPosition);
        _gridShader.Set("uExtent", fadeDistance);
        _gridShader.Set("uFadeDistance", fadeDistance);
        _gl.BindVertexArray(_gridVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);

        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
    }

    public void EndFrame()
    {
        EnsurePhase(Phase.Overlays);
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)_previousFramebuffer);
        _gl.Viewport(_previousViewport[0], _previousViewport[1], (uint)_previousViewport[2], (uint)_previousViewport[3]);
        _target = null;
        _environment = null;
        _phase = Phase.None;
    }

    private void EnsurePhase(Phase phase)
    {
        if (_phase != phase)
            throw new InvalidOperationException(phase == Phase.Scene
                ? "Draw the scene between BeginFrame and PostProcess."
                : "Draw overlays between PostProcess and EndFrame.");
    }

    public void Dispose()
    {
        _meshShader.Dispose();
        _unlitShader.Dispose();
        _gridShader.Dispose();
        _skyShader.Dispose();
        _postProcessor.Dispose();
        _gl.DeleteVertexArray(_gridVao);
        _gl.DeleteBuffer(_gridVbo);
        _gl.DeleteVertexArray(_emptyVao);
    }
}

using System.Numerics;
using Silk.NET.OpenGL;

namespace Menhir.Editor.Rendering.OpenGL;

/// <summary>OpenGL 3.3 Core implementation of <see cref="IRenderer"/>.</summary>
public sealed unsafe class GLRenderer : IRenderer
{
    private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.6f));

    private readonly GL _gl;
    private readonly GLShaderProgram _meshShader;
    private readonly GLShaderProgram _gridShader;
    private readonly uint _gridVao;
    private readonly uint _gridVbo;

    private GLRenderTarget? _currentTarget;
    private ViewParams _view;
    private Matrix4x4 _viewProjection;
    private int _previousFramebuffer;
    private readonly int[] _previousViewport = new int[4];

    public GLRenderer(GL gl)
    {
        _gl = gl;
        _meshShader = new GLShaderProgram(gl, Shaders.MeshVertex, Shaders.MeshFragment);
        _gridShader = new GLShaderProgram(gl, Shaders.GridVertex, Shaders.GridFragment);

        float[] corners = [-1, -1, 1, -1, 1, 1, -1, -1, 1, 1, -1, 1];
        _gridVao = gl.GenVertexArray();
        gl.BindVertexArray(_gridVao);
        _gridVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _gridVbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, corners, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
        gl.BindVertexArray(0);
    }

    public IRenderTarget CreateRenderTarget(int width, int height) => new GLRenderTarget(_gl, width, height);

    public IMesh CreateMesh(MeshData data) => new GLMesh(_gl, data);

    public void BeginPass(IRenderTarget target, in ViewParams view, Vector4 clearColor)
    {
        if (_currentTarget != null)
            throw new InvalidOperationException("EndPass was not called for the previous pass.");
        _currentTarget = (GLRenderTarget)target;
        _view = view;
        _viewProjection = view.View * view.Projection;

        _gl.GetInteger(GetPName.DrawFramebufferBinding, out _previousFramebuffer);
        _gl.GetInteger(GetPName.Viewport, _previousViewport);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _currentTarget.Framebuffer);
        _gl.Viewport(0, 0, (uint)target.Width, (uint)target.Height);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);

        _gl.ClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    public void DrawMesh(IMesh mesh, in Matrix4x4 model, Vector4 color)
    {
        EnsureInPass();

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
        _meshShader.Set("uColor", color);
        _meshShader.Set("uLightDirection", LightDirection);
        ((GLMesh)mesh).Draw();
    }

    public void DrawGrid()
    {
        EnsureInPass();

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

    public void EndPass()
    {
        EnsureInPass();
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)_previousFramebuffer);
        _gl.Viewport(_previousViewport[0], _previousViewport[1], (uint)_previousViewport[2], (uint)_previousViewport[3]);
        _currentTarget = null;
    }

    private void EnsureInPass()
    {
        if (_currentTarget == null)
            throw new InvalidOperationException("Call BeginPass first.");
    }

    public void Dispose()
    {
        _meshShader.Dispose();
        _gridShader.Dispose();
        _gl.DeleteVertexArray(_gridVao);
        _gl.DeleteBuffer(_gridVbo);
    }
}

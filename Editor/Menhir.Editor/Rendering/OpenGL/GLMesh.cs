using Silk.NET.OpenGL;

namespace Menhir.Editor.Rendering.OpenGL;

internal sealed unsafe class GLMesh : IMesh
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;

    public uint IndexCount { get; }

    /// <summary>Interleaved layout: position (location 0), normal (location 1).</summary>
    public GLMesh(GL gl, MeshData data)
    {
        if (data.Positions.Length != data.Normals.Length)
            throw new ArgumentException("Positions and normals must have the same length.", nameof(data));

        _gl = gl;
        IndexCount = (uint)data.Indices.Length;

        var vertices = new float[data.Positions.Length * 6];
        for (int i = 0; i < data.Positions.Length; i++)
        {
            vertices[i * 6 + 0] = data.Positions[i].X;
            vertices[i * 6 + 1] = data.Positions[i].Y;
            vertices[i * 6 + 2] = data.Positions[i].Z;
            vertices[i * 6 + 3] = data.Normals[i].X;
            vertices[i * 6 + 4] = data.Normals[i].Y;
            vertices[i * 6 + 5] = data.Normals[i].Z;
        }

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);

        _ebo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, data.Indices, BufferUsageARB.StaticDraw);

        const uint stride = 6 * sizeof(float);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));

        gl.BindVertexArray(0);
    }

    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, IndexCount, DrawElementsType.UnsignedInt, null);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
    }
}

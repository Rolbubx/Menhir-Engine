using System.Numerics;
using Silk.NET.OpenGL;

namespace Menhir.Editor.Rendering.OpenGL;

internal sealed unsafe class GLShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _uniformLocations = new();

    public uint Handle { get; }

    public GLShaderProgram(GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;
        uint vertex = Compile(ShaderType.VertexShader, vertexSource);
        uint fragment = Compile(ShaderType.FragmentShader, fragmentSource);

        Handle = gl.CreateProgram();
        gl.AttachShader(Handle, vertex);
        gl.AttachShader(Handle, fragment);
        gl.LinkProgram(Handle);
        gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out int linked);
        string linkLog = gl.GetProgramInfoLog(Handle);

        gl.DetachShader(Handle, vertex);
        gl.DetachShader(Handle, fragment);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);

        if (linked == 0)
            throw new InvalidOperationException($"Shader link failed: {linkLog}");
    }

    private uint Compile(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"{type} compilation failed: {log}");
        }
        return shader;
    }

    public void Use() => _gl.UseProgram(Handle);

    // System.Numerics matrices are row-major with row vectors; GL reads them column-major,
    // so uploading without transposing lets GLSL write `M * v` for the same transform.
    public void Set(string name, in Matrix4x4 value)
    {
        fixed (Matrix4x4* ptr = &value)
            _gl.UniformMatrix4(Location(name), 1, false, (float*)ptr);
    }

    public void Set(string name, float value) => _gl.Uniform1(Location(name), value);

    public void Set(string name, Vector3 value) => _gl.Uniform3(Location(name), value.X, value.Y, value.Z);

    public void Set(string name, Vector4 value) => _gl.Uniform4(Location(name), value.X, value.Y, value.Z, value.W);

    private int Location(string name)
    {
        if (!_uniformLocations.TryGetValue(name, out int location))
        {
            location = _gl.GetUniformLocation(Handle, name);
            _uniformLocations[name] = location;
        }
        return location;
    }

    public void Dispose() => _gl.DeleteProgram(Handle);
}

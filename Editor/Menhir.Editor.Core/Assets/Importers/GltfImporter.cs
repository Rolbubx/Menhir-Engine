using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Menhir.Editor.Rendering;

namespace Menhir.Editor.Assets.Importers;

/// <summary>
/// glTF 2.0 (.gltf with embedded or external buffers, and binary .glb): the triangles of every mesh
/// in the default scene, placed by their nodes' transforms, go into one mesh. Materials, texture
/// coordinates, skins and animations are ignored for now; compressed meshes (Draco, meshopt) are refused.
/// </summary>
public static class GltfImporter
{
    private const uint GlbMagic = 0x46546C67;   // "glTF"
    private const uint JsonChunk = 0x4E4F534A;  // "JSON"
    private const uint BinChunk = 0x004E4942;   // "BIN\0"

    /// <param name="file">The .gltf or .glb file's bytes.</param>
    /// <param name="loadFile">Reads a file next to the asset (external buffers), by its relative URI.</param>
    public static MeshData Import(byte[] file, Func<string, byte[]> loadFile)
    {
        byte[]? glbBinary = null;
        ReadOnlySpan<byte> json = file;
        if (file.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(file) == GlbMagic)
            json = SplitGlb(file, out glbBinary);

        using JsonDocument document = JsonDocument.Parse(json.ToArray());
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("extensionsRequired", out JsonElement required) && required.GetArrayLength() > 0)
            throw new NotSupportedException($"The file needs glTF extensions Menhir does not support: " +
                                            string.Join(", ", required.EnumerateArray().Select(e => e.GetString())) + ".");

        var buffers = new List<byte[]>();
        foreach (JsonElement buffer in Array(root, "buffers"))
        {
            if (!buffer.TryGetProperty("uri", out JsonElement uri))
                buffers.Add(glbBinary ?? throw new InvalidDataException("A buffer has no data."));
            else
                buffers.Add(LoadUri(uri.GetString()!, loadFile));
        }

        var gltf = new Gltf(root, buffers);
        var builder = new MeshBuilder();
        List<JsonElement> nodes = Array(root, "nodes").ToList();
        List<JsonElement> scenes = Array(root, "scenes").ToList();
        if (scenes.Count > 0)
        {
            int sceneIndex = root.TryGetProperty("scene", out JsonElement s) ? s.GetInt32() : 0;
            foreach (JsonElement node in Array(scenes[sceneIndex], "nodes"))
                AddNode(gltf, nodes, node.GetInt32(), Matrix4x4.Identity, builder, depth: 0);
        }
        else
        {
            foreach (JsonElement mesh in Array(root, "meshes"))
                AddMesh(gltf, mesh, Matrix4x4.Identity, builder);
        }

        if (builder.TriangleCount == 0)
            throw new InvalidDataException("The file has no triangles.");
        return builder.Build();
    }

    private static ReadOnlySpan<byte> SplitGlb(byte[] file, out byte[]? binary)
    {
        binary = null;
        ReadOnlySpan<byte> json = default;
        int offset = 12;
        while (offset + 8 <= file.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset + 4));
            if (length < 0 || offset + 8 + length > file.Length)
                throw new InvalidDataException("A GLB chunk is cut off.");
            if (type == JsonChunk)
                json = file.AsSpan(offset + 8, length);
            else if (type == BinChunk)
                binary = file.AsSpan(offset + 8, length).ToArray();
            offset += 8 + length;
        }
        if (json.IsEmpty)
            throw new InvalidDataException("The GLB file has no JSON chunk.");
        return json;
    }

    private static byte[] LoadUri(string uri, Func<string, byte[]> loadFile)
    {
        if (uri.StartsWith("data:", StringComparison.Ordinal))
        {
            int comma = uri.IndexOf(',');
            if (comma < 0 || !uri[..comma].EndsWith(";base64", StringComparison.Ordinal))
                throw new InvalidDataException("Only base64 data URIs are supported.");
            return Convert.FromBase64String(uri[(comma + 1)..]);
        }
        string path = Uri.UnescapeDataString(uri);
        if (Path.IsPathRooted(path) || path.Split('/', '\\').Contains(".."))
            throw new InvalidDataException($"Buffer '{uri}' is outside the model's folder.");
        return loadFile(path);
    }

    private static void AddNode(Gltf gltf, List<JsonElement> nodes, int index, Matrix4x4 parent, MeshBuilder builder, int depth)
    {
        if (depth > 64 || index < 0 || index >= nodes.Count)
            throw new InvalidDataException("The node hierarchy is invalid.");
        JsonElement node = nodes[index];
        Matrix4x4 world = LocalMatrix(node) * parent;
        if (node.TryGetProperty("mesh", out JsonElement mesh))
            AddMesh(gltf, Array(gltf.Root, "meshes").ElementAt(mesh.GetInt32()), world, builder);
        foreach (JsonElement child in Array(node, "children"))
            AddNode(gltf, nodes, child.GetInt32(), world, builder, depth + 1);
    }

    /// <summary>A node's matrix (glTF's column-major array is System.Numerics' row-major layout) or its TRS.</summary>
    private static Matrix4x4 LocalMatrix(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out JsonElement m))
        {
            float[] a = m.EnumerateArray().Select(e => e.GetSingle()).ToArray();
            return new Matrix4x4(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7], a[8], a[9], a[10], a[11], a[12], a[13], a[14], a[15]);
        }
        Vector3 t = node.TryGetProperty("translation", out JsonElement te) ? Vec3(te) : Vector3.Zero;
        Quaternion r = node.TryGetProperty("rotation", out JsonElement re)
            ? new Quaternion(re[0].GetSingle(), re[1].GetSingle(), re[2].GetSingle(), re[3].GetSingle())
            : Quaternion.Identity;
        Vector3 s = node.TryGetProperty("scale", out JsonElement se) ? Vec3(se) : Vector3.One;
        return Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
    }

    private static void AddMesh(Gltf gltf, JsonElement mesh, Matrix4x4 world, MeshBuilder builder)
    {
        Matrix4x4 normalMatrix = Matrix4x4.Invert(world, out Matrix4x4 inverse) ? Matrix4x4.Transpose(inverse) : world;
        bool mirrored = world.GetDeterminant() < 0f;

        foreach (JsonElement primitive in Array(mesh, "primitives"))
        {
            int mode = primitive.TryGetProperty("mode", out JsonElement me) ? me.GetInt32() : 4;
            if (mode is not (4 or 5 or 6))
                continue; // points and lines
            JsonElement attributes = primitive.GetProperty("attributes");
            if (!attributes.TryGetProperty("POSITION", out JsonElement positionAccessor))
                continue;
            Vector3[] positions = gltf.ReadVec3(positionAccessor.GetInt32());
            Vector3[]? normals = attributes.TryGetProperty("NORMAL", out JsonElement n) ? gltf.ReadVec3(n.GetInt32()) : null;
            uint[] indices = primitive.TryGetProperty("indices", out JsonElement ie)
                ? gltf.ReadIndices(ie.GetInt32())
                : Enumerable.Range(0, positions.Length).Select(i => (uint)i).ToArray();

            (Vector3, Vector3?) Corner(uint i)
            {
                if (i >= positions.Length)
                    throw new InvalidDataException("An index refers to a vertex that does not exist.");
                Vector3 p = Vector3.Transform(positions[i], world);
                Vector3? normal = normals != null ? Vector3.TransformNormal(normals[i], normalMatrix) : null;
                return (p, normal);
            }

            void Triangle(uint a, uint b, uint c)
            {
                if (mirrored)
                    (b, c) = (c, b);
                builder.AddTriangle(Corner(a), Corner(b), Corner(c));
            }

            for (int i = 0; i + 2 < indices.Length; i += mode == 4 ? 3 : 1)
            {
                if (mode == 4)
                    Triangle(indices[i], indices[i + 1], indices[i + 2]);
                else if (mode == 5) // strip: every other triangle is flipped
                {
                    if (i % 2 == 0)
                        Triangle(indices[i], indices[i + 1], indices[i + 2]);
                    else
                        Triangle(indices[i + 1], indices[i], indices[i + 2]);
                }
                else // fan
                    Triangle(indices[0], indices[i + 1], indices[i + 2]);
            }
        }
    }

    private static Vector3 Vec3(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());

    private static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];

    /// <summary>Reads accessors through their buffer views.</summary>
    private sealed class Gltf(JsonElement root, List<byte[]> buffers)
    {
        public JsonElement Root { get; } = root;

        private (byte[] Data, int Offset, int Stride, int Count, int ComponentType) Accessor(int index, string type, int elementSize)
        {
            JsonElement accessor = Array(Root, "accessors").ElementAt(index);
            if (accessor.TryGetProperty("sparse", out _))
                throw new NotSupportedException("Sparse accessors are not supported.");
            if (accessor.GetProperty("type").GetString() != type)
                throw new InvalidDataException($"Accessor {index} is not a {type}.");
            int count = accessor.GetProperty("count").GetInt32();
            int componentType = accessor.GetProperty("componentType").GetInt32();
            if (!accessor.TryGetProperty("bufferView", out JsonElement viewIndex))
                return (new byte[count * elementSize], 0, elementSize, count, componentType); // all zeros
            JsonElement view = Array(Root, "bufferViews").ElementAt(viewIndex.GetInt32());
            byte[] data = buffers[view.GetProperty("buffer").GetInt32()];
            int offset = (view.TryGetProperty("byteOffset", out JsonElement vo) ? vo.GetInt32() : 0) +
                         (accessor.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0);
            int stride = view.TryGetProperty("byteStride", out JsonElement st) ? st.GetInt32() : elementSize;
            if (count > 0 && (offset < 0 || (long)offset + (long)stride * (count - 1) + elementSize > data.Length))
                throw new InvalidDataException($"Accessor {index} reads past the end of its buffer.");
            return (data, offset, stride, count, componentType);
        }

        public Vector3[] ReadVec3(int index)
        {
            var (data, offset, stride, count, componentType) = Accessor(index, "VEC3", 12);
            if (componentType != 5126)
                throw new NotSupportedException("Only float positions and normals are supported (no quantized meshes).");
            var result = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> p = data.AsSpan(offset + i * stride);
                result[i] = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(p), BinaryPrimitives.ReadSingleLittleEndian(p[4..]),
                    BinaryPrimitives.ReadSingleLittleEndian(p[8..]));
            }
            return result;
        }

        public uint[] ReadIndices(int index)
        {
            JsonElement accessor = Array(Root, "accessors").ElementAt(index);
            int size = accessor.GetProperty("componentType").GetInt32() switch
            {
                5121 => 1,
                5123 => 2,
                5125 => 4,
                _ => throw new InvalidDataException("Indices must be unsigned bytes, shorts or ints."),
            };
            var (data, offset, stride, count, _) = Accessor(index, "SCALAR", size);
            var result = new uint[count];
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> p = data.AsSpan(offset + i * stride);
                result[i] = size == 1 ? p[0] : size == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(p) : BinaryPrimitives.ReadUInt32LittleEndian(p);
            }
            return result;
        }
    }
}

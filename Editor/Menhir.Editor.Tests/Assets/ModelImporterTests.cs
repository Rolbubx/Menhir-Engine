using System.Numerics;
using System.Text;
using Menhir.Editor.Assets.Importers;
using Menhir.Editor.Rendering;

namespace Menhir.Editor.Tests.Assets;

public class ModelImporterTests
{
    private const string Cube = """
        v -0.5 -0.5  0.5
        v  0.5 -0.5  0.5
        v  0.5  0.5  0.5
        v -0.5  0.5  0.5
        v -0.5 -0.5 -0.5
        v  0.5 -0.5 -0.5
        v  0.5  0.5 -0.5
        v -0.5  0.5 -0.5
        f 1 2 3 4
        f 6 5 8 7
        f 5 1 4 8
        f 2 6 7 3
        f 4 3 7 8
        f 5 6 2 1
        """;

    /// <summary>Every triangle's winding agrees with its normals (counter-clockwise seen from outside).</summary>
    private static void AssertOutwardWinding(MeshData mesh)
    {
        for (int t = 0; t < mesh.Indices.Length; t += 3)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t]], b = mesh.Positions[mesh.Indices[t + 1]], c = mesh.Positions[mesh.Indices[t + 2]];
            Vector3 face = Vector3.Cross(b - a, c - a);
            Assert.True(Vector3.Dot(face, mesh.Normals[mesh.Indices[t]]) > 0f);
        }
    }

    [Fact]
    public void Obj_SplitsQuadsAndKeepsHardEdges()
    {
        MeshData mesh = ObjImporter.Import(Cube);

        Assert.Equal(36, mesh.Indices.Length);
        Assert.Equal(24, mesh.Positions.Length); // 90° edges stay hard: 4 corners per face
        Assert.Equal(new Vector3(-0.5f), mesh.Bounds.Min);
        Assert.Equal(new Vector3(0.5f), mesh.Bounds.Max);
        AssertOutwardWinding(mesh);
    }

    [Fact]
    public void Obj_SmoothsShallowAngles()
    {
        // Two triangles folded by 20°: the shared edge is smoothed.
        MeshData mesh = ObjImporter.Import("""
            v 0 0 0
            v 1 0 0
            v 0 0 -1
            v -1 0.364 0
            f 1 2 3
            f 1 3 4
            """);
        Assert.Equal(4, mesh.Positions.Length);
        Vector3 shared = mesh.Normals[Array.IndexOf(mesh.Positions, Vector3.Zero)];
        Assert.True(shared.X > 0f && shared.Y > 0.9f); // between the two faces
    }

    [Fact]
    public void Obj_UsesGivenNormalsAndNegativeIndices()
    {
        MeshData mesh = ObjImporter.Import("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vn 0 0 2
            f -3//-1 -2//-1 -1//-1
            """);
        Assert.All(mesh.Normals, n => Assert.Equal(Vector3.UnitZ, n));
    }

    [Theory]
    [InlineData("f 1 2 3")]
    [InlineData("v 0 0 0\nv 1 0 0\nf 1 2")]
    [InlineData("v 0 0 zero")]
    [InlineData("v 0 0 0")]
    public void Obj_RejectsBrokenFiles(string text) =>
        Assert.Throws<InvalidDataException>(() => ObjImporter.Import(text));

    /// <summary>A .gltf with one triangle in an embedded buffer, under a node moved by +5 on x.</summary>
    private static string TriangleGltf(string nodeExtra = "\"translation\": [5, 0, 0]")
    {
        float[] positions = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        ushort[] indices = [0, 1, 2];
        byte[] buffer = new byte[36 + 6];
        Buffer.BlockCopy(positions, 0, buffer, 0, 36);
        Buffer.BlockCopy(indices, 0, buffer, 36, 6);
        string data = Convert.ToBase64String(buffer);
        return $$"""
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [{ "nodes": [0] }],
              "nodes": [{ "mesh": 0, {{nodeExtra}} }],
              "meshes": [{ "primitives": [{ "attributes": { "POSITION": 0 }, "indices": 1 }] }],
              "buffers": [{ "byteLength": 42, "uri": "data:application/octet-stream;base64,{{data}}" }],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 6 }
              ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
                { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ]
            }
            """;
    }

    [Fact]
    public void Gltf_PlacesMeshesWithTheirNodes()
    {
        MeshData mesh = GltfImporter.Import(Encoding.UTF8.GetBytes(TriangleGltf()), _ => throw new FileNotFoundException());

        Assert.Equal([new Vector3(5, 0, 0), new Vector3(6, 0, 0), new Vector3(5, 1, 0)], mesh.Positions);
        Assert.All(mesh.Normals, n => Assert.Equal(Vector3.UnitZ, n));
    }

    [Fact]
    public void Gltf_MirroredNodesKeepFrontFacesOutside()
    {
        MeshData mesh = GltfImporter.Import(Encoding.UTF8.GetBytes(TriangleGltf("\"scale\": [-1, 1, 1]")), _ => []);
        AssertOutwardWinding(mesh);
        Assert.All(mesh.Normals, n => Assert.Equal(Vector3.UnitZ, n));
    }

    [Fact]
    public void Glb_ReadsTheBinaryChunk()
    {
        // The same triangle as a GLB: the JSON points at the BIN chunk (a buffer without a uri).
        string json = TriangleGltf().Replace("\"uri\": \"data:application/octet-stream;base64,", "\"x\": \"");
        byte[] buffer = Convert.FromBase64String(System.Text.RegularExpressions.Regex.Match(TriangleGltf(), "base64,([^\"]+)").Groups[1].Value);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json.PadRight((json.Length + 3) / 4 * 4));
        byte[] bin = buffer.Concat(new byte[(4 - buffer.Length % 4) % 4]).ToArray();
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(0x46546C67u);
        w.Write(2u);
        w.Write((uint)(12 + 8 + jsonBytes.Length + 8 + bin.Length));
        w.Write((uint)jsonBytes.Length);
        w.Write(0x4E4F534Au);
        w.Write(jsonBytes);
        w.Write((uint)bin.Length);
        w.Write(0x004E4942u);
        w.Write(bin);

        MeshData mesh = GltfImporter.Import(stream.ToArray(), _ => throw new FileNotFoundException());
        Assert.Equal(3, mesh.Positions.Length);
        Assert.Equal(new Vector3(5, 0, 0), mesh.Bounds.Min);
    }

    [Fact]
    public void Gltf_RefusesCompressedMeshes()
    {
        string json = TriangleGltf().Replace("\"asset\"", "\"extensionsRequired\": [\"KHR_draco_mesh_compression\"], \"asset\"");
        Assert.Throws<NotSupportedException>(() => GltfImporter.Import(Encoding.UTF8.GetBytes(json), _ => []));
    }

    [Fact]
    public void ModelImporter_RefusesFbxWithAHint()
    {
        string path = Path.Combine(Path.GetTempPath(), $"menhir-{Guid.NewGuid():N}.fbx");
        File.WriteAllText(path, "not really");
        try
        {
            var e = Assert.Throws<NotSupportedException>(() => ModelImporter.Import(path));
            Assert.Contains("glTF", e.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SandboxCrate_Imports()
    {
        string? engine = Menhir.Editor.Build.PlayerBuilder.FindEngineDirectory(AppContext.BaseDirectory);
        string crate = Path.Combine(Path.GetDirectoryName(engine!)!, "Samples", "Sandbox", "Assets", "Models", "Crate.obj");
        MeshData mesh = ModelImporter.Import(crate);
        Assert.Equal(12, mesh.Indices.Length / 3);
        AssertOutwardWinding(mesh);
    }
}

using Menhir.Editor.Rendering;

namespace Menhir.Editor.Assets.Importers;

/// <summary>Imports a model file (.obj, .gltf, .glb) into one mesh, by its extension.</summary>
public static class ModelImporter
{
    public static bool CanImport(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".obj" or ".gltf" or ".glb";

    /// <exception cref="InvalidDataException">The file is not a valid model.</exception>
    /// <exception cref="NotSupportedException">The format or a feature it uses is not supported (FBX, Draco...).</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static MeshData Import(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        try
        {
            return extension switch
            {
                ".obj" => ObjImporter.Import(File.ReadAllText(path)),
                ".gltf" or ".glb" => GltfImporter.Import(File.ReadAllBytes(path),
                    relative => File.ReadAllBytes(Path.Combine(directory, relative))),
                ".fbx" => throw new NotSupportedException("FBX is not supported; export the model as glTF (.glb) or OBJ."),
                _ => throw new NotSupportedException($"{extension} files are not models."),
            };
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or KeyNotFoundException or ArgumentOutOfRangeException
                                      or FormatException or IndexOutOfRangeException or InvalidOperationException)
        {
            throw new InvalidDataException(e.Message, e);
        }
    }
}

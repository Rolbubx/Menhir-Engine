namespace Menhir.Editor.Assets;

/// <summary>
/// Importer names written in .meta files. Textures (PNG) and models (OBJ, glTF, see
/// <see cref="Importers.ModelImporter"/>) are imported when they are used; the others only record
/// which importer will handle the asset once importing exists.
/// </summary>
public static class AssetImporters
{
    public const string Default = "Default";
    public const string Folder = "Folder";
    public const string Scene = "Scene";
    public const string Script = "Script";
    public const string Texture = "Texture";
    public const string Model = "Model";

    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".scene"] = Scene,
        [".c"] = Script,
        [".h"] = Script,
        [".cpp"] = Script,
        [".hpp"] = Script,
        [".png"] = Texture,
        [".jpg"] = Texture,
        [".jpeg"] = Texture,
        [".tga"] = Texture,
        [".bmp"] = Texture,
        [".obj"] = Model,
        [".gltf"] = Model,
        [".glb"] = Model,
        [".fbx"] = Model,
    };

    public static string ForFile(string fileName) =>
        ByExtension.GetValueOrDefault(Path.GetExtension(fileName), Default);
}

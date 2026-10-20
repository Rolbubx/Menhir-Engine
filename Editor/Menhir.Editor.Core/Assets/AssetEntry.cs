namespace Menhir.Editor.Assets;

/// <summary>A file or folder under Assets/, with its .meta.</summary>
public sealed class AssetEntry
{
    public AssetEntry(string path, bool isFolder, AssetMeta meta)
    {
        Path = path;
        IsFolder = isFolder;
        Meta = meta;
    }

    /// <summary>Project-relative path with forward slashes, e.g. "Assets/Textures/Rock.png".</summary>
    public string Path { get; }

    public string Name => AssetPaths.GetName(Path);

    public bool IsFolder { get; }

    public AssetMeta Meta { get; }

    public Guid Guid => Meta.Guid;
}

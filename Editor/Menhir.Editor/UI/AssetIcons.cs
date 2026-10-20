using System.Numerics;
using Menhir.Editor.Assets;

namespace Menhir.Editor.UI;

/// <summary>Icon and tint of each kind of asset, by importer.</summary>
public static class AssetIcons
{
    public static string Icon(AssetEntry entry, bool open = false) => entry.Meta.Importer switch
    {
        AssetImporters.Folder => open ? Icons.FolderOpen : Icons.Folder,
        AssetImporters.Scene => Icons.Scene,
        AssetImporters.Texture => Icons.Texture,
        AssetImporters.Model => Icons.Model,
        AssetImporters.Script => Icons.Script,
        _ => entry.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? Icons.Text : Icons.File,
    };

    public static Vector4 Color(AssetEntry entry) => Color(entry.Meta.Importer);

    public static Vector4 Color(string importer) => importer switch
    {
        AssetImporters.Folder => EditorTheme.Rgb(0xC8, 0xA8, 0x6A),
        AssetImporters.Scene => EditorTheme.Rgb(0x7F, 0xA8, 0xE6),
        AssetImporters.Texture => EditorTheme.Rgb(0xB5, 0x8C, 0xDE),
        AssetImporters.Model => EditorTheme.Rgb(0x7F, 0xC4, 0x8A),
        AssetImporters.Script => EditorTheme.Rgb(0x6C, 0xC6, 0xC9),
        _ => EditorTheme.Rgb(0xA8, 0xA8, 0xA8),
    };
}

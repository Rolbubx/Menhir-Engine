namespace Menhir.Editor.Assets;

/// <summary>
/// Helpers for asset paths: relative to the project root, always with '/' separators,
/// starting with "Assets" (the root folder, which has no .meta).
/// </summary>
public static class AssetPaths
{
    public const string Root = "Assets";

    public static string Combine(string folder, string name) => $"{folder}/{name}";

    public static string GetName(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    /// <summary>Parent folder path, or null for the Assets root.</summary>
    public static string? GetParent(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? null : path[..slash];
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or inside it.</summary>
    public static bool IsSameOrInside(string path, string folder) =>
        path == folder || path.StartsWith(folder + "/", StringComparison.Ordinal);

    /// <summary>
    /// Files the asset database skips, like Unity does: hidden files (".git", ".DS_Store"),
    /// editor backups ("name~"), temporary files and the .meta files themselves.
    /// </summary>
    public static bool IsIgnored(string name) =>
        name.StartsWith('.') ||
        name.EndsWith('~') ||
        name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(AssetMeta.Extension, StringComparison.OrdinalIgnoreCase);
}

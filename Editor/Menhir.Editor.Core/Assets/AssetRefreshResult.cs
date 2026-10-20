namespace Menhir.Editor.Assets;

/// <summary>What <see cref="AssetDatabase.Refresh"/> changed on disk, for the editor log.</summary>
public sealed class AssetRefreshResult
{
    /// <summary>Assets that got a new .meta file.</summary>
    public List<string> CreatedMetas { get; } = new();

    /// <summary>.meta files deleted because their asset is gone.</summary>
    public List<string> RemovedMetas { get; } = new();

    /// <summary>Assets whose .meta was unreadable or had a duplicate GUID, and got a new GUID.</summary>
    public List<string> RepairedMetas { get; } = new();

    /// <summary>Files or folders that could not be read or written.</summary>
    public List<string> Errors { get; } = new();

    public bool HasChanges => CreatedMetas.Count + RemovedMetas.Count + RepairedMetas.Count > 0;
}

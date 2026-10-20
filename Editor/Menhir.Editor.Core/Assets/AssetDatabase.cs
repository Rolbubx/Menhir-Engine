namespace Menhir.Editor.Assets;

/// <summary>
/// Index of everything under a project's Assets/ folder. <see cref="Refresh"/> scans the
/// disk and keeps the .meta files in sync: it creates missing ones, deletes orphans and
/// gives a new GUID to copies. Rename, move and delete go through this class so that the
/// .meta always follows its asset and the GUID is kept.
/// </summary>
public sealed class AssetDatabase
{
    private static readonly char[] InvalidNameChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0'];

    private readonly Dictionary<string, AssetEntry> _byPath = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, AssetEntry> _byGuid = new();
    private readonly Dictionary<string, List<AssetEntry>> _children = new(StringComparer.Ordinal);

    public AssetDatabase(string projectRoot)
    {
        ProjectRoot = Path.GetFullPath(projectRoot);
    }

    public string ProjectRoot { get; }

    public string AssetsDirectory => ToAbsolutePath(AssetPaths.Root);

    public int Count => _byPath.Count;

    public string ToAbsolutePath(string assetPath) =>
        Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));

    public AssetEntry? FindByPath(string assetPath) => _byPath.GetValueOrDefault(assetPath);

    public AssetEntry? FindByGuid(Guid guid) => _byGuid.GetValueOrDefault(guid);

    public bool IsFolder(string assetPath) =>
        assetPath == AssetPaths.Root || FindByPath(assetPath)?.IsFolder == true;

    /// <summary>Direct children of a folder: folders first, then files, by name.</summary>
    public IReadOnlyList<AssetEntry> GetChildren(string folderPath) =>
        _children.TryGetValue(folderPath, out List<AssetEntry>? children) ? children : [];

    /// <summary>Rescans Assets/ and fixes the .meta files. Creates Assets/ if it is missing.</summary>
    public AssetRefreshResult Refresh()
    {
        var result = new AssetRefreshResult();
        _byPath.Clear();
        _byGuid.Clear();
        _children.Clear();

        Directory.CreateDirectory(AssetsDirectory);
        ScanFolder(AssetPaths.Root, result);
        return result;
    }

    public string CreateFolder(string parentFolder, string name = "New Folder")
    {
        RequireFolder(parentFolder);
        name = ValidateName(name);

        string path = AssetPaths.Combine(parentFolder, name);
        for (int i = 1; ExistsOnDisk(path); i++)
            path = AssetPaths.Combine(parentFolder, $"{name} {i}");

        Directory.CreateDirectory(ToAbsolutePath(path));
        Refresh();
        return path;
    }

    /// <summary>Renames a file or folder and its .meta. Returns the new path.</summary>
    public string Rename(string assetPath, string newName)
    {
        AssetEntry entry = RequireEntry(assetPath);
        newName = ValidateName(newName);
        string destination = AssetPaths.Combine(AssetPaths.GetParent(assetPath)!, newName);
        if (destination == assetPath)
            return assetPath;

        // A case-only rename ("rock.png" → "Rock.png") targets the same file on Windows and macOS.
        bool caseOnly = string.Equals(destination, assetPath, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && ExistsOnDisk(destination))
            throw new IOException($"'{destination}' already exists.");

        MoveOnDisk(entry, destination);
        Refresh();
        return destination;
    }

    /// <summary>Moves a file or folder (with its .meta) into another folder. Returns the new path.</summary>
    public string Move(string assetPath, string destinationFolder)
    {
        AssetEntry entry = RequireEntry(assetPath);
        RequireFolder(destinationFolder);
        if (AssetPaths.IsSameOrInside(destinationFolder, assetPath))
            throw new InvalidOperationException($"Cannot move '{assetPath}' into itself.");

        string destination = AssetPaths.Combine(destinationFolder, entry.Name);
        if (destination == assetPath)
            return assetPath;
        if (ExistsOnDisk(destination))
            throw new IOException($"'{destination}' already exists.");

        MoveOnDisk(entry, destination);
        Refresh();
        return destination;
    }

    /// <summary>Deletes a file or a folder with everything in it, and the .meta files. Cannot be undone.</summary>
    public void Delete(string assetPath)
    {
        AssetEntry entry = RequireEntry(assetPath);
        string absolute = ToAbsolutePath(assetPath);
        if (entry.IsFolder)
            Directory.Delete(absolute, recursive: true);
        else
            File.Delete(absolute);
        File.Delete(absolute + AssetMeta.Extension);
        Refresh();
    }

    private void ScanFolder(string folderPath, AssetRefreshResult result)
    {
        string directory = ToAbsolutePath(folderPath);
        var children = new List<AssetEntry>();
        _children[folderPath] = children;

        List<string> names;
        try
        {
            names = Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).ToList()!;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            result.Errors.Add($"{folderPath}: {e.Message}");
            return;
        }
        // Ordinal order makes "which copy keeps the GUID" deterministic.
        names.Sort(StringComparer.Ordinal);

        foreach (string name in names)
        {
            if (AssetPaths.IsIgnored(name))
                continue;

            string path = AssetPaths.Combine(folderPath, name);
            bool isFolder = Directory.Exists(Path.Combine(directory, name));
            AssetMeta meta = LoadOrCreateMeta(path, isFolder, result);
            var entry = new AssetEntry(path, isFolder, meta);
            _byPath[path] = entry;
            _byGuid[meta.Guid] = entry;
            children.Add(entry);

            if (isFolder)
                ScanFolder(path, result);
        }

        RemoveOrphanMetas(folderPath, names, result);

        children.Sort((a, b) => a.IsFolder != b.IsFolder
            ? (a.IsFolder ? -1 : 1)
            : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
    }

    private AssetMeta LoadOrCreateMeta(string assetPath, bool isFolder, AssetRefreshResult result)
    {
        string metaPath = ToAbsolutePath(assetPath) + AssetMeta.Extension;
        string importer = isFolder ? AssetImporters.Folder : AssetImporters.ForFile(AssetPaths.GetName(assetPath));

        AssetMeta? meta = null;
        bool existed = File.Exists(metaPath);
        if (existed)
        {
            try
            {
                meta = AssetMeta.Load(metaPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                result.Errors.Add($"{assetPath}{AssetMeta.Extension}: {e.Message}");
            }
        }

        bool duplicate = meta != null && _byGuid.ContainsKey(meta.Guid);
        if (meta != null && !duplicate)
            return meta;

        if (duplicate)
        {
            // A copy made outside the editor (file + .meta): the copy gets its own identity.
            meta!.Guid = Guid.NewGuid();
        }
        else
        {
            meta = AssetMeta.Create(importer);
        }

        try
        {
            meta.Save(metaPath);
            (existed ? result.RepairedMetas : result.CreatedMetas).Add(assetPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            result.Errors.Add($"{assetPath}{AssetMeta.Extension}: {e.Message}");
        }
        return meta;
    }

    private void RemoveOrphanMetas(string folderPath, List<string> names, AssetRefreshResult result)
    {
        var assetNames = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!name.EndsWith(AssetMeta.Extension, StringComparison.OrdinalIgnoreCase))
                continue;
            string assetName = name[..^AssetMeta.Extension.Length];
            if (assetName.Length == 0 || assetNames.Contains(assetName))
                continue;

            string path = AssetPaths.Combine(folderPath, name);
            try
            {
                File.Delete(ToAbsolutePath(path));
                result.RemovedMetas.Add(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                result.Errors.Add($"{path}: {e.Message}");
            }
        }
    }

    private void MoveOnDisk(AssetEntry entry, string destination)
    {
        string source = ToAbsolutePath(entry.Path);
        string target = ToAbsolutePath(destination);
        if (entry.IsFolder)
            Directory.Move(source, target);
        else
            File.Move(source, target);

        string sourceMeta = source + AssetMeta.Extension;
        if (File.Exists(sourceMeta))
            File.Move(sourceMeta, target + AssetMeta.Extension);
    }

    private bool ExistsOnDisk(string assetPath)
    {
        string absolute = ToAbsolutePath(assetPath);
        return File.Exists(absolute) || Directory.Exists(absolute);
    }

    private AssetEntry RequireEntry(string assetPath) =>
        FindByPath(assetPath) ?? throw new ArgumentException($"Unknown asset '{assetPath}'.", nameof(assetPath));

    private void RequireFolder(string folderPath)
    {
        if (!IsFolder(folderPath))
            throw new ArgumentException($"'{folderPath}' is not a folder.", nameof(folderPath));
    }

    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name == "." || name == "..")
            throw new ArgumentException("The name is empty.", nameof(name));
        if (name.IndexOfAny(InvalidNameChars) >= 0)
            throw new ArgumentException($"'{name}' contains a character that is not allowed in file names.", nameof(name));
        if (AssetPaths.IsIgnored(name))
            throw new ArgumentException($"'{name}' would be ignored by the asset database.", nameof(name));
        return name;
    }
}

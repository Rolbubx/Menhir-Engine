using Menhir.Editor.Assets;

namespace Menhir.Editor.Projects;

/// <summary>
/// A project folder:
/// <list type="bullet">
/// <item>Assets/: the game's files, each with a .meta (committed)</item>
/// <item>ProjectSettings/: project-wide settings (committed)</item>
/// <item>Cache/: data the editor can rebuild, ignored by git</item>
/// </list>
/// </summary>
public sealed class Project
{
    private Project(string rootPath)
    {
        RootPath = rootPath;
        Assets = new AssetDatabase(rootPath);
    }

    public string RootPath { get; }

    public string Name => Path.GetFileName(RootPath);

    public string ProjectSettingsDirectory => Path.Combine(RootPath, "ProjectSettings");

    public string CacheDirectory => Path.Combine(RootPath, "Cache");

    public AssetDatabase Assets { get; }

    /// <summary>Opens an existing project. The folder must contain an Assets/ folder.</summary>
    public static Project Open(string rootPath)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (!Directory.Exists(Path.Combine(fullPath, AssetPaths.Root)))
            throw new DirectoryNotFoundException($"'{fullPath}' is not a Menhir project (no Assets folder).");

        Directory.CreateDirectory(Path.Combine(fullPath, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(fullPath, "Cache"));
        return new Project(fullPath);
    }
}

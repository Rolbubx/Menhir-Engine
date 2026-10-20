using Menhir.Editor.Assets;
using Menhir.Editor.Scenes;
using Menhir.Editor.Serialization;

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
    public const string ProjectSettingsFolder = "ProjectSettings";
    public const string CacheFolder = "Cache";

    private Project(string rootPath, ProjectSettings settings)
    {
        RootPath = rootPath;
        Settings = settings;
        Assets = new AssetDatabase(rootPath);
    }

    public string RootPath { get; }

    public ProjectSettings Settings { get; }

    public string Name => string.IsNullOrWhiteSpace(Settings.Name) ? Path.GetFileName(RootPath) : Settings.Name;

    public string ProjectSettingsDirectory => Path.Combine(RootPath, ProjectSettingsFolder);

    public string CacheDirectory => Path.Combine(RootPath, CacheFolder);

    public AssetDatabase Assets { get; }

    private string SettingsFile => Path.Combine(ProjectSettingsDirectory, ProjectSettings.FileName);

    private string EditorStateFile => Path.Combine(CacheDirectory, ProjectEditorState.FileName);

    public static bool IsProjectFolder(string path) => Directory.Exists(Path.Combine(path, AssetPaths.Root));

    /// <summary>Reads a project's settings without opening it (nothing is created), or null if there are none.</summary>
    public static ProjectSettings? ReadSettings(string rootPath) =>
        JsonFile.Load<ProjectSettings>(Path.Combine(rootPath, ProjectSettingsFolder, ProjectSettings.FileName));

    /// <summary>Opens an existing project. The folder must contain an Assets/ folder.</summary>
    public static Project Open(string rootPath)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (!IsProjectFolder(fullPath))
            throw new DirectoryNotFoundException($"'{fullPath}' is not a Menhir project (no Assets folder).");

        Directory.CreateDirectory(Path.Combine(fullPath, ProjectSettingsFolder));
        Directory.CreateDirectory(Path.Combine(fullPath, CacheFolder));
        ProjectSettings settings =
            JsonFile.Load<ProjectSettings>(Path.Combine(fullPath, ProjectSettingsFolder, ProjectSettings.FileName))
            ?? new ProjectSettings();
        return new Project(fullPath, settings);
    }

    /// <summary>
    /// Creates a project in a new folder <paramref name="parentDirectory"/>/<paramref name="name"/>
    /// with a default scene at Assets/Scenes/Main.scene (see <see cref="SceneTemplates"/>), and opens it.
    /// </summary>
    /// <param name="template">Defaults to <see cref="ProjectTemplates.UniversalPipeline"/>.</param>
    /// <param name="remote">Remote to share the project through, or null for a local project.</param>
    public static Project Create(string parentDirectory, string name, ProjectTemplate? template = null,
        ProjectRemoteSettings? remote = null)
    {
        name = name.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new ArgumentException($"'{name}' is not a valid folder name.", nameof(name));
        if (remote != null && !ProjectRemoteSettings.IsValidUrl(remote.Url, out string? urlError))
            throw new ArgumentException(urlError, nameof(remote));
        if (!Directory.Exists(parentDirectory))
            throw new DirectoryNotFoundException($"'{parentDirectory}' does not exist.");

        string root = Path.Combine(Path.GetFullPath(parentDirectory), name);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException($"'{root}' already exists and is not empty.");

        Directory.CreateDirectory(Path.Combine(root, AssetPaths.Root, "Scenes"));
        Directory.CreateDirectory(Path.Combine(root, CacheFolder));
        var settings = new ProjectSettings
        {
            Name = name,
            Type = (template ?? ProjectTemplates.UniversalPipeline).Id,
            Remote = remote,
        };
        if (remote != null)
            remote.Url = remote.Url.Trim();
        JsonFile.Save(Path.Combine(root, ProjectSettingsFolder, ProjectSettings.FileName), settings);
        File.WriteAllText(Path.Combine(root, ".gitignore"), $"{CacheFolder}/\n");
        File.WriteAllText(Path.Combine(root, AssetPaths.Root, "Scenes", "Main" + SceneSerializer.Extension),
            SceneSerializer.Serialize(SceneTemplates.CreateDefault()));

        return Open(root);
    }

    public void SaveSettings() => JsonFile.Save(SettingsFile, Settings);

    public ProjectEditorState LoadEditorState() => JsonFile.Load<ProjectEditorState>(EditorStateFile) ?? new();

    public void SaveEditorState(ProjectEditorState state)
    {
        try
        {
            JsonFile.Save(EditorStateFile, state);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Only convenience data: losing it just means the editor opens another scene next time.
        }
    }
}

using Menhir.Editor.Assets;
using Menhir.Editor.Commands;
using Menhir.Editor.Logging;
using Menhir.Editor.Projects;
using Menhir.Editor.Scenes;
using Menhir.Editor.Serialization;

namespace Menhir.Editor;

/// <summary>Editor state shared by the panels: open project, open scene and selection.</summary>
public sealed class EditorContext
{
    private Scene _activeScene = new();
    private long _selectedEntityId;
    private Guid? _selectedAssetGuid;

    /// <summary>The open project, or null when the editor was started without one.</summary>
    public Project? Project { get; private set; }

    public Scene ActiveScene
    {
        get => _activeScene;
        private set
        {
            _activeScene = value;
            SelectedEntityId = 0;
        }
    }

    /// <summary>GUID of the active scene's file, or null for a scene that was never saved.</summary>
    public Guid? ActiveSceneGuid { get; private set; }

    /// <summary>Path of the active scene's file (it follows renames), or null when unsaved or deleted.</summary>
    public string? ActiveScenePath =>
        ActiveSceneGuid is { } guid ? Project?.Assets.FindByGuid(guid)?.Path : null;

    /// <summary>Undo/redo steps of the active scene. Every scene edit goes through <see cref="Execute"/>.</summary>
    public CommandHistory History { get; } = new();

    /// <summary>True when the active scene has changes that are not saved.</summary>
    public bool IsSceneDirty => History.IsDirty;

    public void Execute(ICommand command) => History.Execute(command);

    // One thing is selected at a time: selecting an entity clears the asset selection and vice versa.

    /// <summary>LocalId of the selected entity in the active scene, 0 when nothing is selected.</summary>
    public long SelectedEntityId
    {
        get => _selectedEntityId;
        set
        {
            _selectedEntityId = value;
            if (value != 0)
                _selectedAssetGuid = null;
        }
    }

    public Entity? SelectedEntity => ActiveScene.Find(SelectedEntityId);

    /// <summary>GUID of the selected asset (stable across renames and moves), or null.</summary>
    public Guid? SelectedAssetGuid
    {
        get => _selectedAssetGuid;
        set
        {
            _selectedAssetGuid = value;
            if (value != null)
                _selectedEntityId = 0;
        }
    }

    public AssetEntry? SelectedAsset =>
        SelectedAssetGuid is { } guid ? Project?.Assets.FindByGuid(guid) : null;

    /// <summary>Opens a project and the scene that was open last time (or its first scene).</summary>
    public void OpenProject(Project project)
    {
        Project = project;
        SelectedAssetGuid = null;
        RefreshAssets();

        Guid? lastScene = project.LoadEditorState().LastScene;
        AssetEntry? scene = lastScene is { } guid ? project.Assets.FindByGuid(guid) : null;
        scene ??= FindSceneAssets().FirstOrDefault();
        if (scene == null || !OpenScene(scene))
            NewScene();
    }

    /// <summary>All .scene files of the project, by path.</summary>
    public IEnumerable<AssetEntry> FindSceneAssets()
    {
        if (Project == null)
            return [];
        return Project.Assets.GetAllEntries()
            .Where(e => !e.IsFolder && e.Meta.Importer == AssetImporters.Scene)
            .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase);
    }

    public void NewScene()
    {
        ActiveScene = SceneTemplates.CreateDefault();
        ActiveSceneGuid = null;
        History.Clear();
    }

    /// <summary>Loads a scene file. Returns false (and logs why) when it cannot be read.</summary>
    public bool OpenScene(AssetEntry asset)
    {
        string path = Project!.Assets.ToAbsolutePath(asset.Path);
        var warnings = new List<string>();
        Scene scene;
        try
        {
            scene = SceneSerializer.Deserialize(File.ReadAllText(path), warnings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SceneFormatException)
        {
            Log.Error($"Could not open {asset.Path}: {e.Message}");
            return false;
        }

        foreach (string warning in warnings)
            Log.Warning($"{asset.Path}: {warning}");
        scene.Name = Path.GetFileNameWithoutExtension(asset.Name);
        ActiveScene = scene;
        ActiveSceneGuid = asset.Guid;
        History.Clear();
        RememberActiveScene();
        Log.Info($"Opened scene {asset.Path}");
        return true;
    }

    /// <summary>Saves the active scene to its file. The scene must have been saved once (see <see cref="SaveSceneAs"/>).</summary>
    public void SaveScene()
    {
        string assetPath = ActiveScenePath ?? throw new InvalidOperationException("The scene has no file yet.");
        File.WriteAllText(Project!.Assets.ToAbsolutePath(assetPath), SceneSerializer.Serialize(ActiveScene));
        History.MarkSaved();
        Log.Info($"Saved scene {assetPath}");
    }

    /// <summary>Saves the active scene to a new file <paramref name="folder"/>/<paramref name="name"/>.scene.</summary>
    public void SaveSceneAs(string folder, string name)
    {
        AssetDatabase assets = Project?.Assets ?? throw new InvalidOperationException("No project is open.");
        name = name.Trim();
        if (name.EndsWith(SceneSerializer.Extension, StringComparison.OrdinalIgnoreCase))
            name = name[..^SceneSerializer.Extension.Length];
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new ArgumentException($"'{name}' is not a valid file name.");
        if (!assets.IsFolder(folder))
            throw new ArgumentException($"'{folder}' is not a folder.");

        string assetPath = AssetPaths.Combine(folder, name + SceneSerializer.Extension);
        string absolute = assets.ToAbsolutePath(assetPath);
        if (File.Exists(absolute))
            throw new IOException($"'{assetPath}' already exists.");

        File.WriteAllText(absolute, SceneSerializer.Serialize(ActiveScene));
        RefreshAssets();
        ActiveScene.Name = name;
        ActiveSceneGuid = assets.FindByPath(assetPath)?.Guid;
        History.MarkSaved();
        RememberActiveScene();
        Log.Info($"Saved scene {assetPath}");
    }

    /// <summary>Rescans Assets/ and logs what changed on disk.</summary>
    public void RefreshAssets()
    {
        if (Project == null)
            return;

        AssetRefreshResult result = Project.Assets.Refresh();
        foreach (string path in result.CreatedMetas)
            Log.Info($"Created meta for {path}");
        foreach (string path in result.RemovedMetas)
            Log.Info($"Removed orphan {path}");
        foreach (string path in result.RepairedMetas)
            Log.Warning($"Gave {path} a new GUID (its .meta was unreadable or a duplicate)");
        foreach (string error in result.Errors)
            Log.Error($"Asset refresh: {error}");
    }

    private void RememberActiveScene()
    {
        if (Project != null && ActiveSceneGuid != null)
            Project.SaveEditorState(new ProjectEditorState { LastScene = ActiveSceneGuid });
    }
}

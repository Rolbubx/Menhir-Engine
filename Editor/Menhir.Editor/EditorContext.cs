using Menhir.Editor.Assets;
using Menhir.Editor.Projects;
using Menhir.Editor.Scenes;

namespace Menhir.Editor;

/// <summary>Editor state shared by the panels.</summary>
public sealed class EditorContext
{
    private Scene _activeScene = DemoScene.Create();
    private long _selectedEntityId;
    private Guid? _selectedAssetGuid;

    /// <summary>The open project, or null when the editor was started without one.</summary>
    public Project? Project { get; private set; }

    public Scene ActiveScene
    {
        get => _activeScene;
        set
        {
            _activeScene = value;
            SelectedEntityId = 0;
        }
    }

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

    public void OpenProject(Project project)
    {
        Project = project;
        SelectedAssetGuid = null;
        RefreshAssets();
    }

    /// <summary>Rescans Assets/ and logs what changed on disk.</summary>
    public void RefreshAssets()
    {
        if (Project == null)
            return;

        AssetRefreshResult result = Project.Assets.Refresh();
        foreach (string path in result.CreatedMetas)
            Console.WriteLine($"Created meta for {path}");
        foreach (string path in result.RemovedMetas)
            Console.WriteLine($"Removed orphan {path}");
        foreach (string path in result.RepairedMetas)
            Console.WriteLine($"Warning: gave {path} a new GUID (its .meta was unreadable or a duplicate)");
        foreach (string error in result.Errors)
            Console.Error.WriteLine($"Asset refresh error: {error}");
    }
}

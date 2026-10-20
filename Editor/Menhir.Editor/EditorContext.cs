using Menhir.Editor.Scenes;

namespace Menhir.Editor;

/// <summary>Editor state shared by the panels.</summary>
public sealed class EditorContext
{
    private Scene _activeScene = DemoScene.Create();

    public Scene ActiveScene
    {
        get => _activeScene;
        set
        {
            _activeScene = value;
            SelectedEntityId = 0;
        }
    }

    /// <summary>LocalId of the selected entity in the active scene, 0 when nothing is selected.</summary>
    public long SelectedEntityId { get; set; }

    public Entity? SelectedEntity => ActiveScene.Find(SelectedEntityId);
}

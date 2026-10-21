using Menhir.Editor.Inspection;

namespace Menhir.Editor.Scenes;

public enum LoadSceneMode
{
    /// <summary>Unloads every loaded scene first.</summary>
    Single,

    /// <summary>Adds the scene to the loaded ones (its objects and lights; the sky, camera and effects stay the first scene's).</summary>
    Additive,
}

/// <summary>
/// Built-in behaviour: loads a scene of the build when an Input Manager button is pressed. Runs in
/// the built game; the editor will run it in Play mode.
/// </summary>
[ComponentCategory("Gameplay")]
public sealed class SceneSwitcher : Component
{
    [Tooltip("A button of the Input Manager (Project Settings > Input).")]
    public string Button { get; set; } = "Jump";

    [Tooltip("Name of a scene in the build (its file name without .scene).")]
    public string Scene { get; set; } = "";

    public LoadSceneMode Mode { get; set; } = LoadSceneMode.Single;
}

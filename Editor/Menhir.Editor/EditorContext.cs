using Menhir.Editor.Scenes;

namespace Menhir.Editor;

/// <summary>Editor state shared by the panels.</summary>
public sealed class EditorContext
{
    public Scene ActiveScene { get; set; } = DemoScene.Create();
}

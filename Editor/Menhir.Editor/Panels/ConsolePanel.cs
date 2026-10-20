namespace Menhir.Editor.Panels;

public sealed class ConsolePanel : EditorPanel
{
    public override string Title => "Console";

    protected override void DrawContent() => Placeholder("Editor logs - Phase 8.");
}

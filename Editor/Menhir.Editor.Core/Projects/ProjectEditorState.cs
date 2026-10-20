namespace Menhir.Editor.Projects;

/// <summary>
/// Cache/EditorState.json: what the editor reopens next time (per machine, not committed).
/// Assets are referred to by GUID, so renaming or moving them does not break it.
/// </summary>
public sealed class ProjectEditorState
{
    public const string FileName = "EditorState.json";

    /// <summary>GUID of the scene that was open last, if it was saved.</summary>
    public Guid? LastScene { get; set; }
}

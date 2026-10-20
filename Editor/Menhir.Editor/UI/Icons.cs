namespace Menhir.Editor.UI;

/// <summary>
/// Lucide icons (ISC license, see Resources/Fonts), merged into the editor fonts,
/// so they can be used in any label: <c>$"{Icons.Folder} Assets"</c>.
/// Codepoints come from lucide-static's font/info.json; browse them at https://lucide.dev/icons.
/// </summary>
public static class Icons
{
    // Objects and assets
    public const string Entity = "\ue065"; // box
    public const string Folder = "\ue0dc";
    public const string FolderOpen = "\ue247";
    public const string File = "\ue0c4";
    public const string Scene = "\ue29b"; // clapperboard
    public const string Texture = "\ue0f9"; // image
    public const string Model = "\ue529"; // cuboid
    public const string Script = "\ue0c7"; // file-code
    public const string Text = "\ue0d0"; // file-text
    public const string Transform = "\ue2e5"; // move-3d
    public const string Component = "\ue29c"; // puzzle
    public const string MeshRenderer = "\ue4b8"; // shapes
    public const string MissingComponent = "\ue324"; // file-question

    // Panels
    public const string Hierarchy = "\ue40d"; // list-tree
    public const string Inspector = "\ue0fe"; // info
    public const string Project = "\ue247"; // folder-open
    public const string Console = "\ue185"; // terminal
    public const string Viewport = "\ue2fe"; // axis-3d

    // Actions
    public const string Plus = "\ue141";
    public const string Search = "\ue155";
    public const string Close = "\ue1b2"; // x
    public const string Trash = "\ue18e";
    public const string Copy = "\ue0a2";
    public const string Refresh = "\ue149";
    public const string More = "\ue0bb"; // ellipsis-vertical
    public const string ChevronRight = "\ue073";
    public const string ArrowUp = "\ue04e";
    public const string ListView = "\ue10c"; // list
    public const string GridView = "\ue105"; // layout-grid
    public const string Collapse = "\ue104"; // layers

    // Log levels
    public const string Info = "\ue0fe";
    public const string Warning = "\ue193"; // triangle-alert
    public const string Error = "\ue12b"; // octagon-alert
}

using Menhir.Editor.Inspection;

namespace Menhir.Editor.Projects;

/// <summary>
/// How File > Build makes the game (saved in ProjectSettings.json): which scenes it contains
/// and the window it opens.
/// </summary>
public sealed class BuildSettings
{
    /// <summary>
    /// Asset paths of the scenes in the build, the first one loaded at startup. When empty, every
    /// scene of the project is included, the one open in the editor first.
    /// </summary>
    [HideInInspector]
    public List<string> Scenes { get; set; } = [];

    [Range(64, 7680)]
    public int WindowWidth { get; set; } = 1280;

    [Range(64, 4320)]
    public int WindowHeight { get; set; } = 720;

    /// <summary>
    /// The C compiler (GCC or Clang, a path or a name on the PATH) for the scripts and the game; empty for
    /// the editor's (chosen when Menhir was installed, see Build/Toolchain.cs).
    /// </summary>
    [Tooltip("GCC or Clang: a path, or a name on the PATH. Empty: the editor's compiler.")]
    public string Compiler { get; set; } = "";
}

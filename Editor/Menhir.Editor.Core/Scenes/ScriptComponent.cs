using System.Text.Json;
using Menhir.Editor.Inspection;

namespace Menhir.Editor.Scenes;

/// <summary>
/// A script of the project (a class deriving from Menhir.Script) on an entity: its class name and
/// the values of its public fields, as saved in the scene. The editor runs it in Play mode, the
/// built game after IL2C. Fields left out keep the value the class gives them.
/// </summary>
public sealed class ScriptComponent : Component
{
    /// <summary>Full name of the script class, e.g. "Game.PlayerController" (or "PlayerController" without a namespace).</summary>
    [HideInInspector]
    public string ClassName { get; set; } = "";

    [HideInInspector]
    public Dictionary<string, JsonElement> Fields { get; set; } = new();
}

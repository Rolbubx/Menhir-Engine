using System.Text.Json;
using Menhir.Editor.Inspection;

namespace Menhir.Editor.Scenes;

/// <summary>
/// A script of the project (a C struct declared with MH_SCRIPT, see Docs/Scripting.md) on an entity:
/// its name and the values of its fields, as saved in the scene. Fields left out keep the default
/// the script declares.
/// </summary>
public sealed class ScriptComponent : Component
{
    /// <summary>The script's struct name, e.g. "PlayerController".</summary>
    [HideInInspector]
    public string ScriptName { get; set; } = "";

    [HideInInspector]
    public Dictionary<string, JsonElement> Fields { get; set; } = new();
}

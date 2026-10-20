using System.Text.Json.Nodes;

namespace Menhir.Editor.Scenes;

/// <summary>
/// Stands in for a component whose type is not known (e.g. a script that was renamed or
/// removed). Its saved data is kept as is and written back, so nothing is lost.
/// </summary>
public sealed class MissingComponent : Component
{
    public MissingComponent(string typeName, JsonObject data)
    {
        TypeName = typeName;
        Data = data;
    }

    public string TypeName { get; }

    /// <summary>The component's saved properties, without "type".</summary>
    public JsonObject Data { get; }
}

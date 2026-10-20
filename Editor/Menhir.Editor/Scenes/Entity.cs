namespace Menhir.Editor.Scenes;

public sealed class Entity
{
    /// <summary>ID unique inside its scene file. 0 is never a valid ID.</summary>
    public long LocalId { get; init; }

    /// <summary>LocalId of the parent entity, or 0 for a root entity.</summary>
    public long ParentId { get; set; }

    public string Name { get; set; } = "Entity";

    public Transform Transform { get; } = new();

    public List<Component> Components { get; } = new();

    public T? GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault();
}

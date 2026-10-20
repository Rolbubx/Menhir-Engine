using System.Numerics;

namespace Menhir.Editor.Scenes;

/// <summary>A flat list of entities; the hierarchy is expressed through <see cref="Entity.ParentId"/>.</summary>
public sealed class Scene
{
    private readonly List<Entity> _entities = new();
    private long _nextLocalId = 1;

    public string Name { get; set; } = "Untitled";

    public IReadOnlyList<Entity> Entities => _entities;

    public Entity CreateEntity(string name, long parentId = 0)
    {
        var entity = new Entity { LocalId = _nextLocalId++, Name = name, ParentId = parentId };
        _entities.Add(entity);
        return entity;
    }

    public Entity? Find(long localId) =>
        localId == 0 ? null : _entities.FirstOrDefault(e => e.LocalId == localId);

    /// <summary>Local matrix combined with every ancestor's.</summary>
    public Matrix4x4 GetWorldMatrix(Entity entity)
    {
        Matrix4x4 world = entity.Transform.LocalMatrix;
        // The depth guard protects against accidental parent cycles.
        Entity? parent = Find(entity.ParentId);
        for (int depth = 0; parent != null && depth < _entities.Count; depth++)
        {
            world *= parent.Transform.LocalMatrix;
            parent = Find(parent.ParentId);
        }
        return world;
    }
}

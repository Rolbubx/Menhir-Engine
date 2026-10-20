using System.Numerics;

namespace Menhir.Editor.Scenes;

/// <summary>
/// A flat list of entities; the hierarchy is expressed through <see cref="Entity.ParentId"/>.
/// Siblings are ordered by their position in the list.
/// </summary>
public sealed class Scene
{
    private readonly List<Entity> _entities = new();
    private long _nextLocalId = 1;

    public string Name { get; set; } = "Untitled";

    public IReadOnlyList<Entity> Entities => _entities;

    public Entity CreateEntity(string name, long parentId = 0)
    {
        if (parentId != 0 && Find(parentId) == null)
            throw new ArgumentException($"No entity with id {parentId}.", nameof(parentId));

        var entity = new Entity { LocalId = _nextLocalId++, Name = name, ParentId = parentId };
        _entities.Add(entity);
        return entity;
    }

    public Entity? Find(long localId) =>
        localId == 0 ? null : _entities.FirstOrDefault(e => e.LocalId == localId);

    /// <summary>Direct children of <paramref name="parentId"/> in sibling order; 0 gives the root entities.</summary>
    public IEnumerable<Entity> GetChildren(long parentId) => _entities.Where(e => e.ParentId == parentId);

    public bool HasChildren(Entity entity) => _entities.Any(e => e.ParentId == entity.LocalId);

    /// <summary>True if <paramref name="ancestorId"/> is the parent, grandparent, ... of <paramref name="entity"/>.</summary>
    public bool IsDescendantOf(Entity entity, long ancestorId)
    {
        Entity? parent = Find(entity.ParentId);
        // The depth guard protects against parent cycles in corrupted data.
        for (int depth = 0; parent != null && depth <= _entities.Count; depth++)
        {
            if (parent.LocalId == ancestorId)
                return true;
            parent = Find(parent.ParentId);
        }
        return false;
    }

    /// <summary>An entity cannot become a child of itself or of one of its descendants.</summary>
    public bool CanReparent(Entity entity, long newParentId)
    {
        if (newParentId == 0)
            return true;
        Entity? newParent = Find(newParentId);
        return newParent != null && newParent != entity && !IsDescendantOf(newParent, entity.LocalId);
    }

    /// <summary>
    /// Moves <paramref name="entity"/> under <paramref name="newParentId"/> (0 = root), as its last child.
    /// With <paramref name="keepWorldTransform"/> the entity stays where it is in the world.
    /// </summary>
    public void SetParent(Entity entity, long newParentId, bool keepWorldTransform = true)
    {
        if (!CanReparent(entity, newParentId))
            throw new InvalidOperationException($"Cannot parent '{entity.Name}' to entity {newParentId}.");

        if (keepWorldTransform)
        {
            Matrix4x4 world = GetWorldMatrix(entity);
            Matrix4x4 parentWorld = Find(newParentId) is { } newParent ? GetWorldMatrix(newParent) : Matrix4x4.Identity;
            if (Matrix4x4.Invert(parentWorld, out Matrix4x4 parentInverse))
                entity.Transform.SetFromMatrix(world * parentInverse);
        }

        entity.ParentId = newParentId;
        _entities.Remove(entity);
        _entities.Add(entity);
    }

    /// <summary>Removes the entity and all its descendants. Returns them, the entity first.</summary>
    public IReadOnlyList<Entity> DestroyEntity(Entity entity)
    {
        var removed = new List<Entity> { entity };
        removed.AddRange(_entities.Where(e => IsDescendantOf(e, entity.LocalId)));
        foreach (Entity e in removed)
            _entities.Remove(e);
        return removed;
    }

    /// <summary>Local matrix combined with every ancestor's.</summary>
    public Matrix4x4 GetWorldMatrix(Entity entity)
    {
        Matrix4x4 world = entity.Transform.LocalMatrix;
        Entity? parent = Find(entity.ParentId);
        for (int depth = 0; parent != null && depth <= _entities.Count; depth++)
        {
            world *= parent.Transform.LocalMatrix;
            parent = Find(parent.ParentId);
        }
        return world;
    }
}

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

    /// <summary>ID the next created entity gets. IDs are never reused, even after a delete.</summary>
    public long NextLocalId => _nextLocalId;

    /// <summary>
    /// Builds a scene from loaded entities, in list order. Parents that do not exist (or that
    /// form a cycle) are replaced by 0, so the entity becomes a root; their names are returned in
    /// <paramref name="fixedEntities"/>. Duplicate IDs throw.
    /// </summary>
    public static Scene FromEntities(IEnumerable<Entity> entities, long nextLocalId, out List<Entity> fixedEntities)
    {
        var scene = new Scene();
        foreach (Entity entity in entities)
        {
            if (entity.LocalId <= 0)
                throw new ArgumentException($"Entity '{entity.Name}' has an invalid id {entity.LocalId}.");
            if (scene.Find(entity.LocalId) != null)
                throw new ArgumentException($"Two entities have the id {entity.LocalId}.");
            scene._entities.Add(entity);
            scene._nextLocalId = Math.Max(scene._nextLocalId, entity.LocalId + 1);
        }
        scene._nextLocalId = Math.Max(scene._nextLocalId, nextLocalId);

        fixedEntities = new List<Entity>();
        foreach (Entity entity in scene._entities)
        {
            if (entity.ParentId == 0)
                continue;
            if (scene.Find(entity.ParentId) == null || entity.ParentId == entity.LocalId ||
                scene.IsDescendantOf(entity, entity.LocalId))
            {
                entity.ParentId = 0;
                fixedEntities.Add(entity);
            }
        }
        return scene;
    }

    public Entity CreateEntity(string name, long parentId = 0)
    {
        if (parentId != 0 && Find(parentId) == null)
            throw new ArgumentException($"No entity with id {parentId}.", nameof(parentId));

        var entity = new Entity { LocalId = _nextLocalId++, Name = name, ParentId = parentId };
        _entities.Add(entity);
        return entity;
    }

    public int IndexOf(Entity entity) => _entities.IndexOf(entity);

    /// <summary>
    /// Puts back entities removed earlier (undo of a delete, redo of a create) at their
    /// list index. Indices refer to the list after all the entities are back.
    /// </summary>
    public void RestoreEntities(IEnumerable<(int Index, Entity Entity)> entities)
    {
        List<(int Index, Entity Entity)> sorted = entities.OrderBy(e => e.Index).ToList();
        var incoming = sorted.Select(e => e.Entity.LocalId).ToHashSet();
        foreach ((_, Entity entity) in sorted)
        {
            if (Find(entity.LocalId) != null)
                throw new InvalidOperationException($"An entity with id {entity.LocalId} is already in the scene.");
            if (entity.ParentId != 0 && Find(entity.ParentId) == null && !incoming.Contains(entity.ParentId))
                throw new InvalidOperationException($"Parent {entity.ParentId} of '{entity.Name}' is not in the scene.");
        }

        foreach ((int index, Entity entity) in sorted)
        {
            _entities.Insert(Math.Clamp(index, 0, _entities.Count), entity);
            _nextLocalId = Math.Max(_nextLocalId, entity.LocalId + 1);
        }
    }

    /// <summary>
    /// Play mode: the entities the scene has now (some created, destroyed or deactivated by the scripts),
    /// in that order. Their IDs stay as they are.
    /// </summary>
    public void ReplaceEntities(IEnumerable<Entity> entities)
    {
        _entities.Clear();
        _entities.AddRange(entities);
        foreach (Entity entity in _entities)
            _nextLocalId = Math.Max(_nextLocalId, entity.LocalId + 1);
    }

    /// <summary>Moves an entity to another list index (which changes its sibling order), nothing else.</summary>
    public void MoveEntity(Entity entity, int index)
    {
        if (!_entities.Remove(entity))
            throw new ArgumentException($"'{entity.Name}' is not in this scene.", nameof(entity));
        _entities.Insert(Math.Clamp(index, 0, _entities.Count), entity);
    }

    /// <summary>The entity followed by all its descendants, in list order.</summary>
    public List<Entity> GetSubtree(Entity entity) =>
        _entities.Where(e => e == entity || IsDescendantOf(e, entity.LocalId)).ToList();

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
        removed.AddRange(_entities.Where(e => e != entity && IsDescendantOf(e, entity.LocalId)));
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

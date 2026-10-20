using System.Numerics;
using System.Reflection;
using Menhir.Editor.Inspection;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Commands;

/// <summary>Position, rotation and scale of a <see cref="Transform"/>, as a value.</summary>
public readonly record struct TransformState(Vector3 Position, Quaternion Rotation, Vector3 Scale)
{
    public static TransformState From(Transform transform) => new(transform.Position, transform.Rotation, transform.Scale);

    public void ApplyTo(Transform transform)
    {
        transform.Position = Position;
        transform.Rotation = Rotation;
        transform.Scale = Scale;
    }
}

/// <summary>Creates an entity; redo brings back the same entity (same local ID) at the same place.</summary>
public sealed class CreateEntityCommand : ICommand
{
    private readonly Scene _scene;
    private readonly string _name;
    private readonly long _parentId;
    private readonly Func<IEnumerable<Component>>? _components;
    private int _index;

    public CreateEntityCommand(Scene scene, string name, long parentId = 0, Func<IEnumerable<Component>>? components = null)
    {
        _scene = scene;
        _name = name;
        _parentId = parentId;
        _components = components;
    }

    public string Name => "Create Entity";

    /// <summary>The created entity, once the command has run.</summary>
    public Entity? Entity { get; private set; }

    public void Do()
    {
        if (Entity == null)
        {
            Entity = _scene.CreateEntity(_name, _parentId);
            if (_components != null)
                Entity.Components.AddRange(_components());
            _index = _scene.IndexOf(Entity);
        }
        else
        {
            _scene.RestoreEntities([(_index, Entity)]);
        }
    }

    public void Undo() => _scene.DestroyEntity(Entity!);

    public bool TryMerge(ICommand next) => false;
}

/// <summary>Deletes an entity with its descendants; undo puts them all back in their original order.</summary>
public sealed class DeleteEntityCommand : ICommand
{
    private readonly Scene _scene;
    private readonly Entity _entity;
    private List<(int Index, Entity Entity)> _removed = new();

    public DeleteEntityCommand(Scene scene, Entity entity)
    {
        _scene = scene;
        _entity = entity;
    }

    public string Name => "Delete Entity";

    public void Do()
    {
        _removed = _scene.GetSubtree(_entity).Select(e => (_scene.IndexOf(e), e)).ToList();
        _scene.DestroyEntity(_entity);
    }

    public void Undo() => _scene.RestoreEntities(_removed);

    public bool TryMerge(ICommand next) => false;
}

public sealed class RenameEntityCommand : ICommand
{
    private readonly Entity _entity;
    private readonly string _oldName;
    private readonly string _newName;

    public RenameEntityCommand(Entity entity, string newName)
    {
        _entity = entity;
        _oldName = entity.Name;
        _newName = newName;
    }

    public string Name => "Rename Entity";

    public void Do() => _entity.Name = _newName;

    public void Undo() => _entity.Name = _oldName;

    public bool TryMerge(ICommand next) => false;
}

/// <summary>Moves an entity under another parent, keeping its world transform; undo restores parent, order and local transform.</summary>
public sealed class ReparentEntityCommand : ICommand
{
    private readonly Scene _scene;
    private readonly Entity _entity;
    private readonly long _newParentId;
    private readonly long _oldParentId;
    private readonly int _oldIndex;
    private readonly TransformState _oldTransform;

    public ReparentEntityCommand(Scene scene, Entity entity, long newParentId)
    {
        _scene = scene;
        _entity = entity;
        _newParentId = newParentId;
        _oldParentId = entity.ParentId;
        _oldIndex = scene.IndexOf(entity);
        _oldTransform = TransformState.From(entity.Transform);
    }

    public string Name => "Reparent Entity";

    public void Do() => _scene.SetParent(_entity, _newParentId, keepWorldTransform: true);

    public void Undo()
    {
        _entity.ParentId = _oldParentId;
        _scene.MoveEntity(_entity, _oldIndex);
        _oldTransform.ApplyTo(_entity.Transform);
    }

    public bool TryMerge(ICommand next) => false;
}

/// <summary>Sets position, rotation and scale. Successive edits of the same entity merge (one undo per drag).</summary>
public sealed class SetTransformCommand : ICommand
{
    private readonly Entity _entity;
    private readonly TransformState _before;
    private TransformState _after;

    public SetTransformCommand(Entity entity, TransformState after)
    {
        _entity = entity;
        _before = TransformState.From(entity.Transform);
        _after = after;
    }

    public string Name => "Edit Transform";

    public void Do() => _after.ApplyTo(_entity.Transform);

    public void Undo() => _before.ApplyTo(_entity.Transform);

    public bool TryMerge(ICommand next)
    {
        if (next is not SetTransformCommand other || other._entity != _entity)
            return false;
        _after = other._after;
        return true;
    }
}

/// <summary>Sets one property of a component (inspector edit). Successive edits of the same property merge.</summary>
public sealed class SetPropertyCommand : ICommand
{
    private readonly Component _component;
    private readonly PropertyInfo _property;
    private readonly object? _before;
    private object? _after;

    public SetPropertyCommand(Component component, PropertyInfo property, object? value)
    {
        _component = component;
        _property = property;
        _before = property.GetValue(component);
        _after = value;
    }

    public string Name => $"Edit {InspectableProperties.NicifyName(_property.Name)}";

    public void Do() => _property.SetValue(_component, _after);

    public void Undo() => _property.SetValue(_component, _before);

    public bool TryMerge(ICommand next)
    {
        if (next is not SetPropertyCommand other || other._component != _component || other._property != _property)
            return false;
        _after = other._after;
        return true;
    }
}

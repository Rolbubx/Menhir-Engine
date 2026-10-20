using System.Numerics;
using Menhir.Editor.Commands;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Tests.Commands;

public class SceneCommandsTests
{
    private readonly Scene _scene = new();
    private readonly CommandHistory _history = new();

    [Fact]
    public void CreateEntity_RedoBringsBackTheSameEntity()
    {
        _scene.CreateEntity("First");
        var command = new CreateEntityCommand(_scene, "Cube", components: () => [new MeshRenderer()]);
        _history.Execute(command);
        _scene.CreateEntity("Last"); // not through the history, only to check the index
        Entity created = command.Entity!;

        _history.Undo();
        Assert.Null(_scene.Find(created.LocalId));

        _history.Redo();
        Assert.Same(created, _scene.Find(created.LocalId));
        Assert.Equal(1, _scene.IndexOf(created));
        Assert.NotNull(created.GetComponent<MeshRenderer>());
    }

    [Fact]
    public void DeleteEntity_UndoRestoresSubtreeAndOrder()
    {
        Entity a = _scene.CreateEntity("A");
        Entity parent = _scene.CreateEntity("Parent");
        Entity child = _scene.CreateEntity("Child", parent.LocalId);
        Entity grandchild = _scene.CreateEntity("Grandchild", child.LocalId);
        Entity b = _scene.CreateEntity("B");
        List<Entity> before = _scene.Entities.ToList();

        _history.Execute(new DeleteEntityCommand(_scene, parent));
        Assert.Equal([a, b], _scene.Entities);

        _history.Undo();
        Assert.Equal(before, _scene.Entities);
        Assert.Equal(child.LocalId, grandchild.ParentId);

        _history.Redo();
        Assert.Equal([a, b], _scene.Entities);
    }

    [Fact]
    public void Rename_UndoRestoresTheOldName()
    {
        Entity entity = _scene.CreateEntity("Old");

        _history.Execute(new RenameEntityCommand(entity, "New"));
        Assert.Equal("New", entity.Name);
        _history.Undo();
        Assert.Equal("Old", entity.Name);
    }

    [Fact]
    public void Reparent_UndoRestoresParentOrderAndLocalTransform()
    {
        Entity moved = _scene.CreateEntity("Moved");
        moved.Transform.Position = new Vector3(1, 2, 3);
        Entity other = _scene.CreateEntity("Other");
        Entity parent = _scene.CreateEntity("Parent");
        parent.Transform.Position = new Vector3(10, 0, 0);
        parent.Transform.Scale = new Vector3(2);

        _history.Execute(new ReparentEntityCommand(_scene, moved, parent.LocalId));
        Assert.Equal(parent.LocalId, moved.ParentId);
        Assert.NotEqual(new Vector3(1, 2, 3), moved.Transform.Position);

        _history.Undo();
        Assert.Equal(0, moved.ParentId);
        Assert.Equal([moved, other, parent], _scene.Entities);
        Assert.Equal(new Vector3(1, 2, 3), moved.Transform.Position);
        Assert.Equal(Vector3.One, moved.Transform.Scale);

        _history.Redo();
        Assert.Equal(parent.LocalId, moved.ParentId);
        Assert.Equal(new Vector3(1, 2, 3), _scene.GetWorldMatrix(moved).Translation, new Vector3Comparer());
    }

    [Fact]
    public void SetTransform_ADragIsOneUndoStep()
    {
        Entity entity = _scene.CreateEntity("E");
        for (int i = 1; i <= 10; i++)
        {
            TransformState state = TransformState.From(entity.Transform) with { Position = new Vector3(i, 0, 0) };
            _history.Execute(new SetTransformCommand(entity, state));
        }
        _history.EndMerge();

        Assert.Equal(new Vector3(10, 0, 0), entity.Transform.Position);
        Assert.Equal(1, _history.UndoCount);
        _history.Undo();
        Assert.Equal(Vector3.Zero, entity.Transform.Position);
    }

    [Fact]
    public void SetProperty_UndoRestoresTheValueAndMergesPerProperty()
    {
        var renderer = new MeshRenderer();
        Vector4 original = renderer.Color;
        var color = typeof(MeshRenderer).GetProperty(nameof(MeshRenderer.Color))!;
        var mesh = typeof(MeshRenderer).GetProperty(nameof(MeshRenderer.Mesh))!;

        _history.Execute(new SetPropertyCommand(renderer, color, new Vector4(1, 0, 0, 1)));
        _history.Execute(new SetPropertyCommand(renderer, color, new Vector4(0, 1, 0, 1)));
        _history.Execute(new SetPropertyCommand(renderer, mesh, PrimitiveMesh.Cube));

        Assert.Equal(2, _history.UndoCount);
        Assert.Equal("Edit Mesh", _history.UndoName);
        _history.Undo();
        _history.Undo();
        Assert.Equal(original, renderer.Color);
    }

    [Fact]
    public void AddComponent_UndoRemovesIt()
    {
        Entity entity = _scene.CreateEntity("E");
        var renderer = new MeshRenderer();

        _history.Execute(new AddComponentCommand(entity, renderer));
        Assert.Same(renderer, entity.Components.Single());
        Assert.Equal("Add Mesh Renderer", _history.UndoName);

        _history.Undo();
        Assert.Empty(entity.Components);
        _history.Redo();
        Assert.Same(renderer, entity.Components.Single());
    }

    [Fact]
    public void RemoveComponent_UndoRestoresThePosition()
    {
        Entity entity = _scene.CreateEntity("E");
        var first = new MeshRenderer();
        var middle = new MissingComponent("Rigidbody", new System.Text.Json.Nodes.JsonObject());
        var last = new MeshRenderer();
        entity.Components.AddRange([first, middle, last]);

        _history.Execute(new RemoveComponentCommand(entity, middle));
        Assert.Equal([first, last], entity.Components);
        Assert.Equal("Remove Rigidbody", _history.UndoName);

        _history.Undo();
        Assert.Equal([first, middle, last], entity.Components);
    }

    private sealed class Vector3Comparer : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-4f;
        public int GetHashCode(Vector3 v) => 0;
    }
}

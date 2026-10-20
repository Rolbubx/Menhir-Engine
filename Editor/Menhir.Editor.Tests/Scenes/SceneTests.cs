using System.Numerics;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Tests.Scenes;

public class SceneTests
{
    private const float Tolerance = 1e-4f;

    [Fact]
    public void CreateEntity_AssignsUniqueNonZeroIds()
    {
        var scene = new Scene();
        Entity a = scene.CreateEntity("A");
        Entity b = scene.CreateEntity("B");

        Assert.NotEqual(0, a.LocalId);
        Assert.NotEqual(a.LocalId, b.LocalId);
        Assert.Same(b, scene.Find(b.LocalId));
    }

    [Fact]
    public void CreateEntity_WithUnknownParent_Throws()
    {
        var scene = new Scene();
        Assert.Throws<ArgumentException>(() => scene.CreateEntity("Orphan", parentId: 42));
    }

    [Fact]
    public void GetChildren_ReturnsDirectChildrenInOrder()
    {
        var scene = new Scene();
        Entity root = scene.CreateEntity("Root");
        Entity first = scene.CreateEntity("First", root.LocalId);
        Entity second = scene.CreateEntity("Second", root.LocalId);
        scene.CreateEntity("Grandchild", first.LocalId);

        Assert.Equal([first, second], scene.GetChildren(root.LocalId));
        Assert.Equal([root], scene.GetChildren(0));
    }

    [Fact]
    public void GetWorldMatrix_CombinesParentTransforms()
    {
        var scene = new Scene();
        Entity parent = scene.CreateEntity("Parent");
        parent.Transform.Position = new Vector3(10, 0, 0);
        parent.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        parent.Transform.Scale = new Vector3(2);
        Entity child = scene.CreateEntity("Child", parent.LocalId);
        child.Transform.Position = new Vector3(1, 0, 0);

        Vector3 world = scene.GetWorldMatrix(child).Translation;

        // Local +X, scaled by 2 and turned 90° around Y, points to world -Z.
        AssertClose(new Vector3(10, 0, -2), world);
    }

    [Fact]
    public void SetParent_KeepsWorldTransform()
    {
        var scene = new Scene();
        Entity parent = scene.CreateEntity("Parent");
        parent.Transform.Position = new Vector3(5, 1, -3);
        parent.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
        parent.Transform.Scale = new Vector3(2);
        Entity entity = scene.CreateEntity("Entity");
        entity.Transform.Position = new Vector3(1, 2, 3);
        entity.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f);

        Matrix4x4 before = scene.GetWorldMatrix(entity);
        scene.SetParent(entity, parent.LocalId);

        Assert.Equal(parent.LocalId, entity.ParentId);
        AssertClose(before, scene.GetWorldMatrix(entity));
        AssertClose(new Vector3(0.5f), entity.Transform.Scale);

        scene.SetParent(entity, 0);

        Assert.Equal(0, entity.ParentId);
        AssertClose(before, scene.GetWorldMatrix(entity));
    }

    [Fact]
    public void SetParent_WithoutKeepWorld_KeepsLocalTransform()
    {
        var scene = new Scene();
        Entity parent = scene.CreateEntity("Parent");
        parent.Transform.Position = new Vector3(5, 0, 0);
        Entity entity = scene.CreateEntity("Entity");
        entity.Transform.Position = new Vector3(1, 0, 0);

        scene.SetParent(entity, parent.LocalId, keepWorldTransform: false);

        AssertClose(new Vector3(1, 0, 0), entity.Transform.Position);
        AssertClose(new Vector3(6, 0, 0), scene.GetWorldMatrix(entity).Translation);
    }

    [Fact]
    public void SetParent_MovesEntityToLastChild()
    {
        var scene = new Scene();
        Entity parent = scene.CreateEntity("Parent");
        Entity moved = scene.CreateEntity("Moved");
        Entity existing = scene.CreateEntity("Existing", parent.LocalId);

        scene.SetParent(moved, parent.LocalId);

        Assert.Equal([existing, moved], scene.GetChildren(parent.LocalId));
    }

    [Fact]
    public void SetParent_RejectsSelfAndDescendants()
    {
        var scene = new Scene();
        Entity root = scene.CreateEntity("Root");
        Entity child = scene.CreateEntity("Child", root.LocalId);
        Entity grandchild = scene.CreateEntity("Grandchild", child.LocalId);

        Assert.False(scene.CanReparent(root, root.LocalId));
        Assert.False(scene.CanReparent(root, grandchild.LocalId));
        Assert.False(scene.CanReparent(root, 999));
        Assert.True(scene.CanReparent(grandchild, root.LocalId));
        Assert.True(scene.CanReparent(child, 0));
        Assert.Throws<InvalidOperationException>(() => scene.SetParent(root, child.LocalId));
        Assert.Equal(0, root.ParentId);
    }

    [Fact]
    public void DestroyEntity_RemovesDescendantsOnly()
    {
        var scene = new Scene();
        Entity root = scene.CreateEntity("Root");
        Entity child = scene.CreateEntity("Child", root.LocalId);
        Entity grandchild = scene.CreateEntity("Grandchild", child.LocalId);
        Entity sibling = scene.CreateEntity("Sibling", root.LocalId);
        Entity other = scene.CreateEntity("Other");

        IReadOnlyList<Entity> removed = scene.DestroyEntity(child);

        Assert.Equal([child, grandchild], removed);
        Assert.Equal([root, sibling, other], scene.Entities);
    }

    [Fact]
    public void IdsAreNotReusedAfterDestroy()
    {
        var scene = new Scene();
        Entity first = scene.CreateEntity("First");
        scene.DestroyEntity(first);

        Assert.NotEqual(first.LocalId, scene.CreateEntity("Second").LocalId);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < Tolerance, $"Expected {expected}, got {actual}");

    private static void AssertClose(Matrix4x4 expected, Matrix4x4 actual)
    {
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 4; column++)
            Assert.True(MathF.Abs(expected[row, column] - actual[row, column]) < Tolerance,
                $"Matrices differ at [{row},{column}]: expected {expected}, got {actual}");
    }
}

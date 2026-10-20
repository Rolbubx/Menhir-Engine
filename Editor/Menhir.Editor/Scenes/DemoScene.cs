using System.Numerics;

namespace Menhir.Editor.Scenes;

/// <summary>Placeholder content shown until scenes can be created and loaded.</summary>
public static class DemoScene
{
    public static Scene Create()
    {
        var scene = new Scene { Name = "Demo" };

        AddCube(scene, "Ground Cube", new Vector3(0, 0.5f, 0), Vector3.One, new Vector4(0.531f, 0.531f, 0.531f, 1));
        AddCube(scene, "Red Cube", new Vector3(2.5f, 0.5f, -1), Vector3.One, new Vector4(0.699f, 0.071f, 0.047f, 1));
        AddCube(scene, "Tall Cube", new Vector3(-3, 1.5f, -2), new Vector3(1, 3, 1), new Vector4(0.071f, 0.268f, 0.699f, 1));

        Entity parent = AddCube(scene, "Parent", new Vector3(1, 0.75f, 3), new Vector3(1.5f), new Vector4(0.099f, 0.531f, 0.133f, 1));
        parent.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 6);
        Entity child = AddCube(scene, "Child", new Vector3(0, 0.75f, 0), new Vector3(0.5f), new Vector4(0.893f, 0.612f, 0.071f, 1), parent.LocalId);
        child.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);

        return scene;
    }

    private static Entity AddCube(Scene scene, string name, Vector3 position, Vector3 scale, Vector4 color, long parentId = 0)
    {
        Entity entity = scene.CreateEntity(name, parentId);
        entity.Transform.Position = position;
        entity.Transform.Scale = scale;
        entity.Components.Add(new MeshRenderer { Mesh = PrimitiveMesh.Cube, Color = color });
        return entity;
    }
}

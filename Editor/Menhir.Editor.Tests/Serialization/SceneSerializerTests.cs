using System.Numerics;
using System.Text.Json.Nodes;
using Menhir.Editor.Scenes;
using Menhir.Editor.Serialization;

namespace Menhir.Editor.Tests.Serialization;

public class SceneSerializerTests
{
    public SceneSerializerTests()
    {
        ComponentTypes.Register(typeof(TestComponent).Assembly);
    }

    [Fact]
    public void RoundTrip_EveryComponentTypeWithoutWarnings()
    {
        var scene = new Scene();
        Entity entity = scene.CreateEntity("All");
        foreach (Type type in ComponentTypes.All.OrderBy(t => t.Name))
            entity.Components.Add(ComponentTypes.Create(ComponentTypes.GetName(type))!);

        string json = SceneSerializer.Serialize(scene);
        var warnings = new List<string>();
        Scene loaded = SceneSerializer.Deserialize(json, warnings);

        Assert.Empty(warnings);
        Assert.Equal(entity.Components.Select(c => c.GetType()), loaded.Entities[0].Components.Select(c => c.GetType()));
        Assert.Equal(json, SceneSerializer.Serialize(loaded));
    }

    [Fact]
    public void RoundTrip_KeepsColorCurvesAndTextureReferences()
    {
        var scene = new Scene();
        Entity volume = scene.CreateEntity("Volume");
        var guid = Guid.NewGuid();
        var curves = new Menhir.Editor.PostProcessing.ColorCurves();
        curves.Red = curves.Red.WithPoints([new(0, 0.1f), new(0.5f, 0.7f), new(1, 1)]);
        volume.Components.Add(curves);
        volume.Components.Add(new Menhir.Editor.PostProcessing.ColorLookup { Texture = guid, Contribution = 0.5f });

        Scene loaded = SceneSerializer.Deserialize(SceneSerializer.Serialize(scene), new List<string>());

        var loadedCurves = (Menhir.Editor.PostProcessing.ColorCurves)loaded.Entities[0].Components[0];
        Assert.Equal(curves.Red.Points, loadedCurves.Red.Points);
        Assert.True(loadedCurves.HueVsHue.Loop);
        Assert.Equal(guid, ((Menhir.Editor.PostProcessing.ColorLookup)loaded.Entities[0].Components[1]).Texture);
    }

    [Fact]
    public void RoundTrip_KeepsEntitiesHierarchyAndOrder()
    {
        var scene = new Scene();
        Entity parent = scene.CreateEntity("Parent");
        Entity child = scene.CreateEntity("Child", parent.LocalId);
        Entity other = scene.CreateEntity("Other");
        scene.SetParent(parent, other.LocalId); // parent now comes after its child in the list
        child.Transform.Position = new Vector3(1, 2.5f, -3);
        child.Transform.Rotation = Quaternion.Normalize(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f));
        child.Transform.Scale = new Vector3(0.5f, 2, 1);

        Scene loaded = RoundTrip(scene);

        Assert.Equal(scene.Entities.Select(e => (e.LocalId, e.ParentId, e.Name)),
            loaded.Entities.Select(e => (e.LocalId, e.ParentId, e.Name)));
        Transform t = loaded.Find(child.LocalId)!.Transform;
        Assert.Equal(child.Transform.Position, t.Position);
        Assert.Equal(child.Transform.Rotation, t.Rotation);
        Assert.Equal(child.Transform.Scale, t.Scale);
    }

    [Fact]
    public void RoundTrip_KeepsNextLocalId()
    {
        var scene = new Scene();
        scene.CreateEntity("A");
        Entity b = scene.CreateEntity("B");
        scene.DestroyEntity(b);

        Scene loaded = RoundTrip(scene);

        Assert.Equal(3, loaded.NextLocalId);
        Assert.Equal(3, loaded.CreateEntity("C").LocalId);
    }

    [Fact]
    public void RoundTrip_KeepsEveryComponentPropertyType()
    {
        var scene = new Scene();
        Entity entity = scene.CreateEntity("E");
        var component = new TestComponent
        {
            Flag = true,
            Count = -42,
            Speed = 0.1f,
            Label = "Héllo \"quoted\"",
            Size = new Vector2(1, 2),
            Offset = new Vector3(0.3f, -4, 5e-7f),
            Tint = new Vector4(0.25f, 0.5f, 0.75f, 1),
            Mode = TestMode.FastForward,
            Hidden = 99,
        };
        entity.Components.Add(component);
        entity.Components.Add(new MeshRenderer { Color = new Vector4(1, 0, 0, 1) });

        Entity loaded = RoundTrip(scene).Find(entity.LocalId)!;

        var result = Assert.IsType<TestComponent>(loaded.Components[0]);
        Assert.Equal(component.Flag, result.Flag);
        Assert.Equal(component.Count, result.Count);
        Assert.Equal(component.Speed, result.Speed);
        Assert.Equal(component.Label, result.Label);
        Assert.Equal(component.Size, result.Size);
        Assert.Equal(component.Offset, result.Offset);
        Assert.Equal(component.Tint, result.Tint);
        Assert.Equal(component.Mode, result.Mode);
        Assert.Equal(99, result.Hidden); // hidden from the inspector, but still saved
        Assert.Equal(new Vector4(1, 0, 0, 1), Assert.IsType<MeshRenderer>(loaded.Components[1]).Color);
    }

    [Fact]
    public void Serialize_WritesReadableJson()
    {
        var scene = new Scene();
        Entity entity = scene.CreateEntity("Cube");
        entity.Transform.Position = new Vector3(0, 0.5f, 0);
        entity.Components.Add(new MeshRenderer());

        string json = SceneSerializer.Serialize(scene);

        Assert.Contains("\"name\": \"Cube\"", json);
        Assert.Contains("\"position\": [0, 0.5, 0]", json);
        Assert.Contains("\"rotation\": [0, 0, 0, 1]", json);
        Assert.Contains("\"type\": \"MeshRenderer\"", json);
        Assert.Contains("\"mesh\": \"Cube\"", json);
        Assert.DoesNotContain("\r", json);
    }

    [Fact]
    public void UnknownComponent_IsKeptAndWrittenBack()
    {
        const string json = """
            {
              "version": 1,
              "entities": [
                { "id": 1, "parent": 0, "name": "E", "components": [ { "type": "Rotator", "speed": 3, "axis": [0, 1, 0] } ] }
              ]
            }
            """;
        var warnings = new List<string>();

        Scene scene = SceneSerializer.Deserialize(json, warnings);
        string saved = SceneSerializer.Serialize(scene);
        Scene reloaded = SceneSerializer.Deserialize(saved, new List<string>());

        Assert.Single(warnings);
        var missing = Assert.IsType<MissingComponent>(reloaded.Entities[0].Components[0]);
        Assert.Equal("Rotator", missing.TypeName);
        Assert.Equal(3, (int)missing.Data["speed"]!);
        Assert.Equal(1, (int)missing.Data["axis"]![1]!);
    }

    [Fact]
    public void BadValuesAndMissingParents_AreReportedNotFatal()
    {
        const string json = """
            {
              "version": 1,
              "entities": [
                { "id": 1, "parent": 9, "name": "Lost", "components": [ { "type": "MeshRenderer", "color": "red", "shiny": true } ] }
              ]
            }
            """;
        var warnings = new List<string>();

        Scene scene = SceneSerializer.Deserialize(json, warnings);

        Entity entity = scene.Entities[0];
        Assert.Equal(0, entity.ParentId);
        Assert.Equal(new MeshRenderer().Color, entity.GetComponent<MeshRenderer>()!.Color);
        Assert.Equal(3, warnings.Count); // bad color, unknown property, missing parent
        Assert.Equal(Vector3.One, entity.Transform.Scale); // no transform: defaults
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "entities": [] }""")]
    [InlineData("""{ "version": 99, "entities": [] }""")]
    [InlineData("""{ "version": 1 }""")]
    [InlineData("""{ "version": 1, "entities": [ { "id": 1 }, { "id": 1 } ] }""")]
    [InlineData("""{ "version": 1, "entities": [ { "name": "no id" } ] }""")]
    [InlineData("""{ "version": 1, "entities": [ { "id": 1, "transform": { "position": [1, 2] } } ] }""")]
    public void InvalidFiles_Throw(string json)
    {
        Assert.Throws<SceneFormatException>(() => SceneSerializer.Deserialize(json, new List<string>()));
    }

    private static Scene RoundTrip(Scene scene)
    {
        var warnings = new List<string>();
        Scene loaded = SceneSerializer.Deserialize(SceneSerializer.Serialize(scene), warnings);
        Assert.Empty(warnings);
        return loaded;
    }
}

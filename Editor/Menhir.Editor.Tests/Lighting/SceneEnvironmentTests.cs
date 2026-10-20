using System.Numerics;
using Menhir.Editor.Lighting;
using Menhir.Editor.Mathematics;
using Menhir.Editor.PostProcessing;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Tests.Lighting;

public class SceneEnvironmentTests
{
    [Fact]
    public void WithoutLights_UsesTheDefaultSunAndNoDirectLight()
    {
        SceneEnvironment environment = SceneEnvironment.Gather(new Scene());

        Assert.Equal(SkyModel.DefaultToSun, environment.ToSun);
        Assert.Equal(Vector3.Zero, environment.SunColor);
        Assert.Empty(environment.AdditionalLights);
        Assert.Null(environment.PostProcessing.Bloom);
    }

    [Fact]
    public void FirstDirectionalLight_IsTheSun()
    {
        var scene = new Scene();
        Entity sun = scene.CreateEntity("Sun");
        sun.Transform.Rotation = EulerAngles.ToQuaternion(new Vector3(-90, 0, 0)); // pointing straight down
        sun.Components.Add(new Light { Color = Vector4.One, Intensity = 2 });
        Entity other = scene.CreateEntity("Other");
        other.Components.Add(new Light { Color = new Vector4(1, 0, 0, 1) });

        SceneEnvironment environment = SceneEnvironment.Gather(scene);

        Assert.Equal(1f, environment.ToSun.Y, 4);
        Assert.Equal(new Vector3(2), environment.SunColor);
    }

    [Fact]
    public void PointAndSpotLights_AreInWorldSpaceAndLimited()
    {
        var scene = new Scene();
        Entity parent = scene.CreateEntity("Parent");
        parent.Transform.Position = new Vector3(10, 0, 0);
        Entity lamp = scene.CreateEntity("Lamp", parent.LocalId);
        lamp.Transform.Position = new Vector3(0, 2, 0);
        lamp.Components.Add(new Light { LightType = LightType.Spot, SpotAngle = 60 });
        for (int i = 0; i < 10; i++)
            scene.CreateEntity($"Point {i}").Components.Add(new Light { LightType = LightType.Point });

        SceneEnvironment environment = SceneEnvironment.Gather(scene);

        Assert.Equal(SceneEnvironment.MaxAdditionalLights, environment.AdditionalLights.Count);
        PunctualLight spot = environment.AdditionalLights[0];
        Assert.Equal(new Vector3(10, 2, 0), spot.Position);
        Assert.Equal(MathF.Cos(MathF.PI / 6), spot.SpotCosOuter, 4);
        Assert.Equal(-1f, environment.AdditionalLights[1].SpotCosOuter);
    }

    [Fact]
    public void PostProcessing_UsesTheFirstActiveEffect()
    {
        var scene = new Scene();
        Entity volume = scene.CreateEntity("Volume");
        var off = new Bloom { Active = false, Intensity = 5 };
        var on = new Bloom { Intensity = 1 };
        volume.Components.AddRange([off, on]);

        Assert.Same(on, SceneEnvironment.Gather(scene).PostProcessing.Bloom);
    }

    [Fact]
    public void DefaultScene_HasASunASkyAndAVolume()
    {
        SceneEnvironment environment = SceneEnvironment.Gather(SceneTemplates.CreateDefault());

        Assert.True(environment.ToSun.Y > 0.5f, "the sun is about 50° up");
        Assert.NotEqual(Vector3.Zero, environment.SunColor);
        Assert.Equal(TonemappingMode.ACES, environment.PostProcessing.Tonemapping?.Mode);
        Assert.NotNull(environment.PostProcessing.Bloom);
    }
}

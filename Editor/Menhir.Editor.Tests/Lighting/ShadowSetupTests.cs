using System.Numerics;
using Menhir.Editor.Lighting;
using Menhir.Editor.Mathematics;
using Menhir.Editor.Projects;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Tests.Lighting;

public class ShadowSetupTests
{
    private static readonly Bounds Casters = new(new Vector3(-10, 0, -10), new Vector3(10, 5, 10));

    private static CameraInfo Camera(float yaw = 0f, Vector3? position = null)
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(position ?? new Vector3(0, 2, 10),
            (position ?? new Vector3(0, 2, 10)) + new Vector3(MathF.Sin(yaw), -0.2f, -MathF.Cos(yaw)), Vector3.UnitY);
        Matrix4x4.Invert(view, out Matrix4x4 cameraToWorld);
        return new CameraInfo(cameraToWorld, MathF.PI / 3f, 16f / 9f, 0.1f);
    }

    private static SceneEnvironment Environment(LightShadows shadows = LightShadows.Soft, params Light[] extra)
    {
        var scene = new Scene();
        Entity sun = scene.CreateEntity("Sun");
        sun.Transform.Rotation = EulerAngles.ToQuaternion(new Vector3(-50, -30, 0));
        sun.Components.Add(new Light { Shadows = shadows });
        foreach (Light light in extra)
            scene.CreateEntity("Lamp").Components.Add(light);
        return SceneEnvironment.Gather(scene);
    }

    private static Vector3 ToShadow(ShadowSlice slice, Vector3 world)
    {
        Vector4 p = Vector4.Transform(new Vector4(world, 1), slice.WorldToShadow);
        return new Vector3(p.X, p.Y, p.Z) / p.W;
    }

    private static bool InRect(Vector3 uv, Vector4 rect) =>
        uv.X >= rect.X && uv.X <= rect.Z && uv.Y >= rect.Y && uv.Y <= rect.W && uv.Z >= 0 && uv.Z <= 1;

    [Fact]
    public void MainLight_NeedsShadowsAndCasters()
    {
        var settings = new RenderingSettings();
        Assert.Null(ShadowSetup.MainLight(Environment(LightShadows.None), Camera(), settings, Casters));
        Assert.Null(ShadowSetup.MainLight(Environment(), Camera(), settings, null));
        Assert.Null(ShadowSetup.MainLight(Environment(), Camera(), new RenderingSettings { MainLightShadows = false }, Casters));
        Assert.NotNull(ShadowSetup.MainLight(Environment(), Camera(), settings, Casters));
    }

    [Fact]
    public void Cascades_CoverTheViewAndMapIntoTheirTile()
    {
        var settings = new RenderingSettings();
        CameraInfo camera = Camera();
        MainLightShadows shadows = ShadowSetup.MainLight(Environment(), camera, settings, Casters)!;

        Assert.Equal(4, shadows.Cascades.Count);
        Assert.Equal(3, shadows.Filter); // soft, medium quality
        Vector3 forward = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, camera.CameraToWorld));
        foreach (float distance in new[] { 1f, 5f, 15f, 40f })
        {
            Vector3 point = camera.CameraToWorld.Translation + forward * distance;
            int cascade = Enumerable.Range(0, 4).First(i =>
                Vector3.DistanceSquared(point, new Vector3(shadows.CascadeSpheres[i].X, shadows.CascadeSpheres[i].Y,
                    shadows.CascadeSpheres[i].Z)) < shadows.CascadeSpheres[i].W);
            Assert.True(InRect(ToShadow(shadows.Cascades[cascade], point), shadows.Cascades[cascade].AtlasRect),
                $"point at {distance} m is outside cascade {cascade}'s tile");
        }
        // Farther cascades cover more.
        Assert.True(shadows.CascadeSpheres[3].W > shadows.CascadeSpheres[0].W);
    }

    [Fact]
    public void Cascades_IncludeCastersOutsideTheView()
    {
        var bounds = new Bounds(new Vector3(-10, 0, -10), new Vector3(10, 80, 10));
        MainLightShadows shadows = ShadowSetup.MainLight(Environment(), Camera(), new RenderingSettings(), bounds)!;
        ShadowSlice last = shadows.Cascades[^1];
        Vector3 center = new(shadows.CascadeSpheres[^1].X, shadows.CascadeSpheres[^1].Y, shadows.CascadeSpheres[^1].Z);
        // A point far towards the sun from the cascade's center still lands in front of the near plane.
        Vector3 high = center + Environment().ToSun * 60f;
        Assert.InRange(ToShadow(last, high).Z, 0f, 1f);
    }

    [Fact]
    public void Cascades_DoNotChangeSizeWhenTheCameraTurns()
    {
        var settings = new RenderingSettings();
        MainLightShadows a = ShadowSetup.MainLight(Environment(), Camera(0f), settings, Casters)!;
        MainLightShadows b = ShadowSetup.MainLight(Environment(), Camera(1.3f), settings, Casters)!;
        for (int i = 0; i < 4; i++)
            Assert.Equal(a.CascadeSpheres[i].W, b.CascadeSpheres[i].W, 3);
    }

    [Fact]
    public void Cascades_SoftShadowsUseLargerBiases()
    {
        MainLightShadows soft = ShadowSetup.MainLight(Environment(LightShadows.Soft), Camera(), new RenderingSettings(), Casters)!;
        MainLightShadows hard = ShadowSetup.MainLight(Environment(LightShadows.Hard), Camera(), new RenderingSettings(), Casters)!;
        Assert.Equal(0, hard.Filter);
        Assert.True(soft.Cascades[0].DepthBias < hard.Cascades[0].DepthBias); // more negative
        Assert.True(hard.Cascades[0].DepthBias < 0);
    }

    [Fact]
    public void OneCascade_UsesTheWholeAtlas()
    {
        MainLightShadows shadows = ShadowSetup.MainLight(Environment(), Camera(), new RenderingSettings { CascadeCount = 1 }, Casters)!;
        Assert.Equal(new Vector4(0, 0, 1, 1), shadows.Cascades[0].AtlasRect);
    }

    [Fact]
    public void CascadeEnds_AreIncreasingAndEndAtOne()
    {
        float[] ends = new RenderingSettings { Cascade4Split1 = 0.5f, Cascade4Split2 = 0.2f }.CascadeEnds();
        Assert.Equal(1f, ends[^1]);
        for (int i = 1; i < ends.Length; i++)
            Assert.True(ends[i] > ends[i - 1]);
    }

    [Fact]
    public void AdditionalLights_SpotTakesOneSlicePointSixAndTheAtlasIsLimited()
    {
        SceneEnvironment environment = Environment(LightShadows.Soft,
            new Light { LightType = LightType.Spot, Shadows = LightShadows.Hard },
            new Light { LightType = LightType.Point, Shadows = LightShadows.Soft },
            new Light { LightType = LightType.Point, Shadows = LightShadows.None },
            new Light { LightType = LightType.Point, Shadows = LightShadows.Soft },
            new Light { LightType = LightType.Point, Shadows = LightShadows.Soft });

        AdditionalLightShadows shadows = ShadowSetup.AdditionalLights(environment, new RenderingSettings(), Casters)!;

        Assert.Equal([0, 1, -1, 7, -1], shadows.FirstSlice);
        Assert.Equal(13, shadows.Slices.Count);
        Assert.Equal(0, shadows.Filter[0]);
        Assert.Equal(3, shadows.Filter[1]);
    }

    [Fact]
    public void PointLightFaces_SeeTheirDirection()
    {
        var scene = new Scene();
        Entity lamp = scene.CreateEntity("Lamp");
        lamp.Transform.Position = new Vector3(1, 2, 3);
        lamp.Components.Add(new Light { LightType = LightType.Point, Shadows = LightShadows.Hard, Range = 10 });
        AdditionalLightShadows shadows = ShadowSetup.AdditionalLights(SceneEnvironment.Gather(scene), new RenderingSettings(), Casters)!;

        for (int face = 0; face < 6; face++)
        {
            Vector3 point = new Vector3(1, 2, 3) + ShadowSetup.CubeFaces[face].Forward * 4f;
            Assert.True(InRect(ToShadow(shadows.Slices[face], point), shadows.Slices[face].AtlasRect), $"face {face}");
        }
    }

    [Fact]
    public void Orthographic_MapsNearAndFarToClipDepth()
    {
        Matrix4x4 m = Projections.Orthographic(2, 2, 1, 11);
        Assert.Equal(-1f, Vector4.Transform(new Vector4(0, 0, -1, 1), m).Z, 4);
        Assert.Equal(1f, Vector4.Transform(new Vector4(0, 0, -11, 1), m).Z, 4);
    }
}

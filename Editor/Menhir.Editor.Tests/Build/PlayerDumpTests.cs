using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Menhir.Editor.Assets.Importers;
using Menhir.Editor.Build;
using Menhir.Editor.Lighting;
using Menhir.Editor.Mathematics;
using Menhir.Editor.Projects;
using Menhir.Editor.Rendering;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Tests.Build;

/// <summary>
/// Differential tests: the game player (C, compiled without a window) loads an exported scene and
/// prints what it computes for the first frame; it must match what the editor computes.
/// </summary>
public sealed class PlayerDumpTests : IDisposable
{
    private const int Width = 800, Height = 600;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "menhir-tests", Guid.NewGuid().ToString("N"));

    public PlayerDumpTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static readonly Guid StatueGuid = Guid.Parse("0c2d5b7e8f3a4c1d9e6b2a7f3c8d1e5a");

    // A tall, off-center pyramid: its bounds are not the unit cube's.
    private static readonly MeshData Statue = ObjImporter.Import("""
        v 1 0 1
        v 3 0 1
        v 3 0 3
        v 1 0 3
        v 2 6 2
        f 1 4 3 2
        f 1 2 5
        f 2 3 5
        f 3 4 5
        f 4 1 5
        """);

    private static Scene TestScene()
    {
        var scene = new Scene();
        Entity camera = scene.CreateEntity("Camera");
        camera.Transform.Position = new Vector3(3, 4, 9);
        camera.Transform.Rotation = EulerAngles.ToQuaternion(new Vector3(-20, 15, 2));
        camera.Components.Add(new Camera { FieldOfView = 50, NearClipPlane = 0.2f, FarClipPlane = 200 });

        Entity sun = scene.CreateEntity("Sun");
        sun.Transform.Rotation = EulerAngles.ToQuaternion(new Vector3(-50, -30, 0));
        sun.Components.Add(new Light { Shadows = LightShadows.Soft, Intensity = 1.3f });

        Entity point = scene.CreateEntity("Point");
        point.Transform.Position = new Vector3(-2, 2, 1);
        point.Components.Add(new Light { LightType = LightType.Point, Range = 7, Shadows = LightShadows.Soft });

        Entity spot = scene.CreateEntity("Spot");
        spot.Transform.Position = new Vector3(2, 4, 0);
        spot.Transform.Rotation = EulerAngles.ToQuaternion(new Vector3(-70, 10, 0));
        spot.Components.Add(new Light { LightType = LightType.Spot, SpotAngle = 45, Range = 12, Shadows = LightShadows.Hard });

        Entity ground = scene.CreateEntity("Ground");
        ground.Transform.Scale = new Vector3(20, 0.2f, 20);
        ground.Components.Add(new MeshRenderer { CastShadows = false });

        Entity parent = scene.CreateEntity("Parent");
        parent.Transform.Position = new Vector3(0, 1, 0);
        parent.Transform.Rotation = EulerAngles.ToQuaternion(new Vector3(0, 30, 0));
        parent.Transform.Scale = new Vector3(1, 2, 1);
        parent.Components.Add(new MeshRenderer());
        Entity child = scene.CreateEntity("Child", parent.LocalId);
        child.Transform.Position = new Vector3(1.5f, 0.5f, 0);
        child.Transform.Rotation = EulerAngles.ToQuaternion(new Vector3(10, 20, 30));
        child.Components.Add(new MeshRenderer());

        Entity statue = scene.CreateEntity("Statue");
        statue.Transform.Position = new Vector3(-4, 0, -3);
        statue.Transform.Rotation = EulerAngles.ToQuaternion(new Vector3(0, 40, 0));
        statue.Components.Add(new MeshRenderer { Model = StatueGuid });
        return scene;
    }

    private Dictionary<(string, int), float[]> Dump(Scene scene, RenderingSettings settings)
    {
        string? engine = PlayerBuilder.FindEngineDirectory(AppContext.BaseDirectory);
        Assert.NotNull(engine);
        string data = Path.Combine(_directory, "GameData.binchunk");
        File.WriteAllBytes(data, GameDataExporter.Export("Test", [new BuildScene("Test", scene)], settings, new BuildSettings(),
            _ => null, guid => guid == StatueGuid ? Statue : throw new IOException("no such model"), []));
        string mainC = Path.Combine(_directory, "main.c");
        File.WriteAllText(mainC, PlayerSource.Amalgamate(engine));
        string executable = Path.Combine(_directory, "player");
        new PlayerBuilder(engine).BuildHeadless(mainC, executable);

        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "--data", data, "--dump", "--size", $"{Width}x{Height}" })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);

        var values = new Dictionary<(string, int), float[]>();
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split(' ');
            if (parts[0] == "scene")
                continue;
            values[(parts[0], int.Parse(parts[1]))] = parts[2..].Select(p => float.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        }
        return values;
    }

    private static void Near(IEnumerable<float> expected, float[] actual, string what)
    {
        float[] e = expected.ToArray();
        Assert.True(e.Length == actual.Length, $"{what}: {actual.Length} values instead of {e.Length}");
        for (int i = 0; i < e.Length; i++)
            Assert.True(MathF.Abs(e[i] - actual[i]) <= 1e-4f * MathF.Max(1f, MathF.Abs(e[i])),
                $"{what}[{i}]: expected {e[i]}, the player computed {actual[i]}");
    }

    private static IEnumerable<float> M(Matrix4x4 m) =>
    [
        m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44,
    ];

    private static IEnumerable<float> Slice(ShadowSlice s) =>
        M(s.ViewProjection).Concat(M(s.WorldToShadow))
            .Concat([s.AtlasRect.X, s.AtlasRect.Y, s.AtlasRect.Z, s.AtlasRect.W])
            .Concat([s.AtlasPixels.X, s.AtlasPixels.Y, s.AtlasPixels.Size, s.DepthBias, s.NormalBias]);

    [Fact]
    public void Player_ComputesTheSameFrameAsTheEditor()
    {
        Scene scene = TestScene();
        var settings = new RenderingSettings { SoftShadowQuality = SoftShadowQuality.High };
        Dictionary<(string, int), float[]> dump = Dump(scene, settings);

        for (int i = 0; i < scene.Entities.Count; i++)
            Near(M(scene.GetWorldMatrix(scene.Entities[i])), dump[("world", i)], $"world matrix of {scene.Entities[i].Name}");

        GameView view = GameCamera.Compute(scene, Width / (float)Height);
        Near(M(view.View), dump[("view", 0)], "view");
        Near(M(view.Projection), dump[("projection", 0)], "projection");

        SceneEnvironment environment = SceneEnvironment.Gather(scene);
        Near([environment.ToSun.X, environment.ToSun.Y, environment.ToSun.Z, environment.SunColor.X, environment.SunColor.Y,
            environment.SunColor.Z, (float)environment.SunShadows], dump[("sun", 0)], "sun");
        Assert.Equal(2, environment.AdditionalLights.Count);
        for (int i = 0; i < environment.AdditionalLights.Count; i++)
        {
            PunctualLight l = environment.AdditionalLights[i];
            Near([l.Position.X, l.Position.Y, l.Position.Z, l.Direction.X, l.Direction.Y, l.Direction.Z,
                l.Color.X, l.Color.Y, l.Color.Z, l.Range, l.SpotCosOuter, l.SpotCosInner], dump[("light", i)], $"light {i}");
        }

        Bounds? casters = null;
        foreach (Entity entity in scene.Entities)
        {
            if (entity.GetComponent<MeshRenderer>() is not { CastShadows: true } renderer)
                continue;
            Bounds local = renderer.Model != null ? Statue.Bounds : Primitives.CreateCube().Bounds;
            Bounds bounds = Bounds.Transform(local, scene.GetWorldMatrix(entity));
            casters = casters?.Encapsulate(bounds) ?? bounds;
        }

        Matrix4x4.Invert(view.View, out Matrix4x4 cameraToWorld);
        var camera = new CameraInfo(cameraToWorld, view.FieldOfViewY, Width / (float)Height, view.Near);
        MainLightShadows main = ShadowSetup.MainLight(environment, camera, settings, casters)!;
        Near([main.AtlasSize, main.Strength, main.Filter, main.MaxDistance, main.FadeBorder, main.Cascades.Count],
            dump[("main", 0)], "main light shadows");
        for (int i = 0; i < main.Cascades.Count; i++)
        {
            Near(Slice(main.Cascades[i]), dump[("cascade", i)], $"cascade {i}");
            Vector4 sphere = main.CascadeSpheres[i];
            Near([sphere.X, sphere.Y, sphere.Z, sphere.W], dump[("sphere", i)], $"cascade sphere {i}");
        }

        AdditionalLightShadows additional = ShadowSetup.AdditionalLights(environment, settings, casters)!;
        Near([additional.AtlasSize, additional.Slices.Count], dump[("additional", 0)], "additional light shadows");
        for (int i = 0; i < environment.AdditionalLights.Count; i++)
            Near([additional.FirstSlice[i], additional.Strength[i], additional.Filter[i]], dump[("lightshadow", i)],
                $"shadows of light {i}");
        for (int i = 0; i < additional.Slices.Count; i++)
            Near(Slice(additional.Slices[i]), dump[("slice", i)], $"slice {i}");
    }

    [Fact]
    public void Player_WithoutACamera_LooksFromTheDefaultPlace()
    {
        var scene = new Scene();
        scene.CreateEntity("Cube").Components.Add(new MeshRenderer());
        Dictionary<(string, int), float[]> dump = Dump(scene, new RenderingSettings());

        GameView view = GameCamera.Compute(scene, Width / (float)Height);
        Assert.Equal(new Vector3(0, 1, 10), view.Position);
        Near(M(view.View), dump[("view", 0)], "view");
        Near(M(view.Projection), dump[("projection", 0)], "projection");
        Assert.False(dump.ContainsKey(("main", 0))); // no directional light: no sun shadows
    }
}

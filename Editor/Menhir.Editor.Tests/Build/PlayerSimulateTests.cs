using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Menhir.Editor.Build;
using Menhir.Editor.Input;
using Menhir.Editor.Mathematics;
using Menhir.Editor.Projects;
using Menhir.Editor.Runtime;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Tests.Build;

/// <summary>
/// Differential tests of input and scene changes: the player (without a window) plays a script of
/// input events; its axes and fly camera must match the editor's InputState and FlyCameraBehaviour.
/// </summary>
public sealed class PlayerSimulateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "menhir-tests", Guid.NewGuid().ToString("N"));

    public PlayerSimulateTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static Scene Start()
    {
        var scene = new Scene();
        Entity camera = scene.CreateEntity("Camera");
        camera.Transform.Position = new Vector3(1, 2, 8);
        camera.Transform.Rotation = EulerAngles.ToQuaternion(new Vector3(-10, 25, 0));
        camera.Components.Add(new Camera());
        camera.Components.Add(new FlyCamera { MoveSpeed = 4 });
        Entity switches = scene.CreateEntity("Switches");
        switches.Components.Add(new SceneSwitcher { Button = "Jump", Scene = "Extra", Mode = LoadSceneMode.Additive });
        Entity other = scene.CreateEntity("Other");
        other.Components.Add(new SceneSwitcher { Button = "Fire1", Scene = "Level2" });
        scene.CreateEntity("Cube").Components.Add(new MeshRenderer());
        return scene;
    }

    private static Scene Simple(string name)
    {
        var scene = new Scene();
        scene.CreateEntity(name).Components.Add(new MeshRenderer());
        return scene;
    }

    private List<string> Simulate(Scene start, string script, List<string>? warnings = null)
    {
        string engine = PlayerBuilder.FindEngineDirectory(AppContext.BaseDirectory)!;
        string data = Path.Combine(_directory, "GameData.binchunk");
        BuildScene[] scenes = [new("Start", start), new("Extra", Simple("Extra")), new("Level2", Simple("Level2"))];
        File.WriteAllBytes(data, GameDataExporter.Export("Test", scenes, new RenderingSettings(), new BuildSettings(),
            _ => null, _ => throw new IOException(), warnings ?? [], new InputSettings()));
        string mainC = Path.Combine(_directory, "main.c");
        File.WriteAllText(mainC, PlayerSource.Amalgamate(engine));
        string executable = Path.Combine(_directory, "player");
        new PlayerBuilder(engine).BuildHeadless(mainC, executable);
        string scriptFile = Path.Combine(_directory, "script.txt");
        File.WriteAllText(scriptFile, script);

        var start2 = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "--data", data, "--simulate", scriptFile })
            start2.ArgumentList.Add(argument);
        using Process process = Process.Start(start2)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>The same script played on the editor's side: one block of expected lines per "dump".</summary>
    private static List<(Dictionary<string, float[]> Axes, float[] Fly)> Reference(Scene start, string script)
    {
        var settings = new InputSettings();
        var input = new InputState(settings.Axes);
        Entity cameraEntity = start.Entities[0];
        var transform = new Transform { Position = cameraEntity.Transform.Position, Rotation = cameraEntity.Transform.Rotation };
        var fly = new FlyCameraBehaviour(cameraEntity.GetComponent<FlyCamera>()!, transform);
        var frame = new InputFrame();
        var dumps = new List<(Dictionary<string, float[]>, float[])>();

        foreach (string raw in script.Split('\n'))
        {
            string[] p = raw.Split('#')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0)
                continue;
            float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
            switch (p[0])
            {
                case "down": frame.Down.Add(int.Parse(p[1])); break;
                case "up": frame.Down.Remove(int.Parse(p[1])); break;
                case "mouse": frame.MouseDeltaX = F(1); frame.MouseDeltaY = F(2); break;
                case "scroll": frame.Scroll = F(1); break;
                case "pad": frame.GamepadAxes[int.Parse(p[1])] = F(2); break;
                case "frame":
                    for (int i = 0; i < (p.Length > 2 ? int.Parse(p[2]) : 1); i++)
                    {
                        input.Update(frame, F(1));
                        fly.Update(input, F(1));
                        frame.MouseDeltaX = frame.MouseDeltaY = frame.Scroll = 0;
                    }
                    break;
                case "dump":
                    var axes = new Dictionary<string, float[]>();
                    foreach (string name in settings.Axes.Select(a => a.Name).Distinct())
                        axes[name] = [input.GetAxis(name), input.GetAxisRaw(name), B(input.GetButton(name)),
                            B(input.GetButtonDown(name)), B(input.GetButtonUp(name))];
                    Quaternion q = transform.Rotation;
                    dumps.Add((axes, [fly.Yaw, fly.Pitch, transform.Position.X, transform.Position.Y, transform.Position.Z,
                        q.X, q.Y, q.Z, q.W]));
                    break;
            }
        }
        return dumps;
    }

    private static float B(bool b) => b ? 1f : 0f;

    private static List<List<string>> Blocks(List<string> lines)
    {
        var blocks = new List<List<string>> { new() };
        foreach (string line in lines)
        {
            if (line == "end")
                blocks.Add(new List<string>());
            else
                blocks[^1].Add(line);
        }
        blocks.RemoveAt(blocks.Count - 1);
        return blocks;
    }

    private static void Near(float expected, float actual, string what) =>
        Assert.True(MathF.Abs(expected - actual) <= 1e-4f * MathF.Max(1f, MathF.Abs(expected)),
            $"{what}: expected {expected}, the player computed {actual}");

    [Fact]
    public void AxesButtonsAndFlyCamera_MatchTheEditor()
    {
        const string script = """
            down 87          # w
            frame 0.05 4
            dump
            down 1001        # right mouse button: look
            mouse 12 -7
            frame 0.016
            down 68          # d
            down 340         # left shift: fast
            frame 0.02 10
            dump
            up 87
            up 68
            up 1001
            mouse 30 30      # not looking: ignored
            pad 1 -0.9       # gamepad stick up
            frame 0.016 3
            scroll 2
            down 2003        # gamepad Y: Jump (loads Extra additively, the camera stays)
            frame 0.016
            dump
            """;
        Scene start = Start();
        List<List<string>> blocks = Blocks(Simulate(start, script));
        var expected = Reference(start, script);
        Assert.Equal(expected.Count, blocks.Count);

        for (int d = 0; d < blocks.Count; d++)
        {
            foreach (string line in blocks[d].Where(l => l.StartsWith("axis ", StringComparison.Ordinal)))
            {
                string[] parts = line["axis ".Length..].Split('|');
                float[] values = parts[1].Split(' ').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                float[] want = expected[d].Axes[parts[0]];
                for (int i = 0; i < want.Length; i++)
                    Near(want[i], values[i], $"dump {d}, axis {parts[0]} [{i}]");
            }
            float[] fly = blocks[d].Single(l => l.StartsWith("fly ", StringComparison.Ordinal))
                .Split(' ')[2..].Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            for (int i = 0; i < fly.Length; i++)
                Near(expected[d].Fly[i], fly[i], $"dump {d}, fly camera [{i}]");
        }
        Assert.Contains("cursor 1", blocks[1]);  // looking with the right button held
        Assert.Contains("cursor 0", blocks[2]);
    }

    [Fact]
    public void SceneSwitchers_LoadAdditivelyOnce_ThenReplaceEverything()
    {
        const string script = """
            frame 0.016
            dump
            down 32         # space: Jump loads Extra additively
            frame 0.016
            dump
            up 32
            frame 0.016
            down 32         # already loaded: nothing changes
            frame 0.016
            dump
            down 341        # left ctrl: Fire1 loads Level2 alone
            frame 0.016
            dump
            """;
        List<List<string>> blocks = Blocks(Simulate(Start(), script));
        Assert.Equal(["scenes 0", "scenes 0 1", "scenes 0 1", "scenes 2"],
            blocks.Select(b => b.Single(l => l.StartsWith("scenes", StringComparison.Ordinal))));
        Assert.DoesNotContain(blocks[3], l => l.StartsWith("fly ", StringComparison.Ordinal));
    }

    [Fact]
    public void Export_WarnsAboutSwitchersToScenesOutsideTheBuild()
    {
        Scene start = Start();
        start.Entities[1].GetComponent<SceneSwitcher>()!.Scene = "Nowhere";
        var warnings = new List<string>();
        Simulate(start, "frame 0.016\ndump\n", warnings);
        Assert.Contains(warnings, w => w.Contains("Nowhere"));
    }
}

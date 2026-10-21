using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Menhir.Editor.Build;
using Menhir.Editor.Input;
using Menhir.Editor.Logging;
using Menhir.Editor.Projects;
using Menhir.Editor.Runtime;
using Menhir.Editor.Scenes;
using Menhir.Editor.Scripting;

namespace Menhir.Editor.Tests.Build;

/// <summary>
/// Differential tests of scripts: the same project's scripts run in the editor's Play mode (.NET) and in
/// the built game (IL2C, without a window, playing a script of input events with --simulate). The log
/// must be the same, word for word, and the entities' transforms the same within float rounding.
/// </summary>
public sealed class PlayerScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "menhir-tests", Guid.NewGuid().ToString("N"));

    // The collector is compiled once for all the tests.
    private static readonly string SharedCache = Path.Combine(Path.GetTempPath(), "menhir-tests-cache");

    public PlayerScriptTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string Scripts = """
        using MenhirEngine;
        using System.Collections.Generic;

        public enum Mode { Slow, Fast }

        public class Mover : Script
        {
            public int count = 3;
            public long big = 1L << 40;
            [Range(0f, 10f)] public float speed = 2f;
            public double precise = 0.1;
            public bool active = true;
            public string label = "mover";
            public Mode mode = Mode.Slow;
            public Vector2 offset = new Vector2(1f, 1f);
            public Vector3 direction = Vector3.right;
            public int broken = 4;
            [HideInInspector] public int hidden = 7;
            private readonly List<string> _events = new List<string>();

            public override void Awake()
            {
                _events.Add("awake");
                Debug.Log(name + " awake " + count + " " + big + " " + speed + " " + precise + " " + active + " " + label +
                          " " + mode + " " + offset + " " + direction + " " + broken + " " + hidden);
            }

            public override void Start()
            {
                _events.Add("start");
                Debug.Log(label + " starts at " + transform.position.ToString("F3") + " on frame " + Time.frameCount);
            }

            public override void Update()
            {
                float factor = mode == Mode.Fast ? 2f : 1f;
                transform.position += direction * (speed * factor * Time.deltaTime) +
                                      new Vector3(Input.GetAxis("Horizontal"), 0f, 0f) * Time.deltaTime;
                transform.Rotate(0f, 30f * Time.deltaTime, 0f);
                if (Input.GetKeyDown("space"))
                    Debug.Log("space on frame " + Time.frameCount + ", mouse at " + Input.mousePosition + ", events " +
                              string.Join(",", _events));
                if (Input.GetButtonDown("Fire1"))
                    SceneManager.LoadScene("Second", LoadSceneMode.Additive);
                if (Input.GetKeyDown("q"))
                    Application.Quit();
            }
        }

        namespace Game
        {
            /// <summary>A child that looks at its parent's target and reports where it is.</summary>
            public class Follower : Script
            {
                public float distance = 2f;

                public override void LateUpdate()
                {
                    Entity player = Entity.Find("Player");
                    Mover mover = player.GetComponent<Mover>();
                    transform.LookAt(player.transform.position + new Vector3(0f, 0f, -distance));
                    if (Time.frameCount % 5 == 0)
                        Debug.Log("follower of " + mover.label + " at " + transform.position.ToString("F2") + " parent " +
                                  transform.parent.name + " rotation " + transform.rotation.eulerAngles.ToString("F1"));
                }
            }
        }

        public class Thrower : Script
        {
            private string _nothing;
            private int _updates;

            public override void Update()
            {
                _updates++;
                if (_updates == 2)
                    Debug.Log(_nothing.Length);
                try
                {
                    if (_updates == 3)
                        throw new System.InvalidOperationException("caught it");
                }
                catch (System.InvalidOperationException e)
                {
                    Debug.LogWarning("Thrower: " + e.Message);
                }
                if (_updates == 4)
                {
                    Debug.LogError("Thrower disables itself");
                    enabled = false;
                }
            }
        }

        public class Dice : Script
        {
            public override void Start()
            {
                Random.InitState(42);
                var rolls = new List<int>();
                for (int i = 0; i < 5; i++)
                    rolls.Add(Random.Range(1, 7));
                var lookup = new Dictionary<string, float> { ["pi"] = Mathf.PI, ["e"] = 2.71828f };
                Debug.Log("rolls " + string.Join(" ", rolls) + string.Format(" {0:F4} {1,8:0.00}", lookup["pi"], lookup["e"]));
            }
        }

        public class Spawned : Script
        {
            public override void Awake() => Debug.Log("spawned awake on frame " + Time.frameCount);
            public override void Start() => Debug.Log("spawned start on frame " + Time.frameCount);
        }

        public abstract class NotAScript : Script { }

        public class Fragile : Script
        {
            private readonly int[] _slots = new int[2];

            public Fragile() => _slots[2] = 1;
        }
        """;

    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value);

    private static (Scene Main, Scene Second) Scenes()
    {
        var main = new Scene();
        Entity player = main.CreateEntity("Player");
        player.Transform.Position = new Vector3(1, 0, 2);
        player.Components.Add(new ScriptComponent
        {
            ClassName = "Mover",
            Fields = new()
            {
                ["count"] = J(5), ["big"] = J(12345678901234L), ["speed"] = J(1.5f), ["precise"] = J(0.25),
                ["active"] = J(false), ["label"] = J("hero"), ["mode"] = J("Fast"), ["offset"] = J(new[] { 0.5f, -2f }),
                ["direction"] = J(new[] { 0f, 0f, -1f }), ["broken"] = J("oops"), ["hidden"] = J(99), ["gone"] = J(1),
            },
        });
        Entity child = main.CreateEntity("Child");
        child.ParentId = player.LocalId;
        child.Transform.Position = new Vector3(0, 1, 0);
        child.Components.Add(new ScriptComponent { ClassName = "Game.Follower", Fields = new() { ["distance"] = J(3) } });
        Entity thrower = main.CreateEntity("Thrower");
        thrower.Components.Add(new ScriptComponent { ClassName = "Thrower" });
        thrower.Components.Add(new ScriptComponent { ClassName = "Dice" });
        thrower.Components.Add(new ScriptComponent { ClassName = "Missing" });
        thrower.Components.Add(new ScriptComponent { ClassName = "NotAScript" });
        thrower.Components.Add(new ScriptComponent { ClassName = "Fragile" });

        var second = new Scene();
        second.CreateEntity("Extra").Components.Add(new ScriptComponent { ClassName = "Spawned" });
        return (main, second);
    }

    private const string Input = """
        frame 0.02 3
        dump
        down 32          # space
        pos 100 50
        frame 0.02
        up 32
        down 1000        # left mouse button: Fire1 loads Second additively
        frame 0.02
        up 1000
        down 68          # d: Horizontal
        frame 0.016 10
        up 68
        dump
        down 81          # q: Application.Quit
        frame 0.02 2
        dump
        """;

    [Fact]
    public void Scripts_RunTheSameInTheBuiltGameAsInPlayMode()
    {
        Project project = Project.Create(_root, "Game");
        Directory.CreateDirectory(Path.Combine(project.Assets.AssetsDirectory, "Scripts"));
        File.WriteAllText(Path.Combine(project.Assets.AssetsDirectory, "Scripts", "Scripts.cs"), Scripts);
        (Scene main, Scene second) = Scenes();
        BuildScene[] scenes = [new("Main", main), new("Second", second)];

        List<string> native = Native(project, scenes);
        List<string> editor = Editor(project, scenes);

        Assert.Contains("quit", native);
        Assert.Contains(native, l => l.StartsWith("log 2 Thrower.Update on 'Thrower': NullReferenceException", StringComparison.Ordinal));
        Assert.Contains("log 1 Thrower: the script Missing does not exist (or does not compile).", native);
        Assert.Contains(native, l => l.StartsWith("log 2 Fragile..ctor on 'Thrower': IndexOutOfRangeException", StringComparison.Ordinal));
        Assert.Contains("log 1 Player: the saved value of Mover.broken does not fit the field; it keeps its default.", native);
        Assert.Contains("log 0 Player awake 5 12345678901234 1.5 0.25 False hero Fast (0.50, -2.00) (0.00, 0.00, -1.00) 4 7", native);

        string Show(List<string> lines) => string.Join("\n", lines);
        Assert.True(native.Count == editor.Count, $"The game printed:\n{Show(native)}\n\nPlay mode printed:\n{Show(editor)}");
        for (int i = 0; i < native.Count; i++)
            Assert.True(Same(editor[i], native[i]), $"Line {i}: Play mode printed\n  {editor[i]}\nthe game printed\n  {native[i]}");
    }

    /// <summary>Equal text, numbers equal within float rounding (but log lines word for word).</summary>
    private static bool Same(string expected, string actual)
    {
        if (expected.StartsWith("log ", StringComparison.Ordinal))
            return expected == actual;
        string[] a = expected.Split(' '), b = actual.Split(' ');
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i])
                continue;
            if (!float.TryParse(a[i], CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(b[i], CultureInfo.InvariantCulture, out float y) ||
                MathF.Abs(x - y) > 1e-4f * MathF.Max(1f, MathF.Abs(x)))
                return false;
        }
        return true;
    }

    private List<string> Native(Project project, BuildScene[] scenes)
    {
        string engine = PlayerBuilder.FindEngineDirectory(AppContext.BaseDirectory)!;
        string scriptsC = Path.Combine(_root, "scripts.c");
        Assert.True(GameScripts.Translate(project, engine, scriptsC, _ => { }));

        string data = Path.Combine(_root, "GameData.binchunk");
        var warnings = new List<string>();
        File.WriteAllBytes(data, GameDataExporter.Export("Test", scenes, new RenderingSettings(), new BuildSettings(),
            _ => null, _ => throw new IOException(), warnings, new InputSettings()));
        Assert.Empty(warnings);
        string mainC = Path.Combine(_root, "main.c");
        File.WriteAllText(mainC, PlayerSource.Amalgamate(engine));
        string executable = Path.Combine(_root, "player");
        new PlayerBuilder(engine).BuildHeadless(mainC, executable, scriptsC, SharedCache);
        string input = Path.Combine(_root, "input.txt");
        File.WriteAllText(input, Input);

        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "--data", data, "--simulate", input })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error.Result);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith("axis ", StringComparison.Ordinal)).ToList();
    }

    /// <summary>The same input played in the editor's Play mode, printed like the player's --simulate.</summary>
    private List<string> Editor(Project project, BuildScene[] scenes)
    {
        ScriptCompileResult compiled = ScriptCompiler.CompileForEditor(project, typeof(MenhirEngine.Script).Assembly.Location);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        using ScriptAssembly assembly = ScriptAssembly.Load(compiled.AssemblyPath!);

        var lines = new List<string>();
        using var session = new PlaySession(scenes, 0, new InputSettings(), assembly,
            (level, message) => lines.Add($"log {(int)level} {message}"));
        var frame = new InputFrame();
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        foreach (string raw in Input.Split('\n'))
        {
            string[] p = raw.Split('#')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0)
                continue;
            float Arg(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
            switch (p[0])
            {
                case "down": frame.Down.Add(int.Parse(p[1])); break;
                case "up": frame.Down.Remove(int.Parse(p[1])); break;
                case "pos": frame.MouseX = Arg(1); frame.MouseY = Arg(2); break;
                case "frame":
                    for (int i = 0; i < (p.Length > 2 ? int.Parse(p[2]) : 1) && !session.QuitRequested; i++)
                        session.Update(frame, Arg(1));
                    if (session.QuitRequested)
                    {
                        lines.Add("quit");
                        return lines;
                    }
                    break;
                case "dump":
                    lines.Add("scenes " + string.Join(" ", session.Loaded.Select(s => s.Index)));
                    lines.Add("cursor 0");
                    for (int si = 0; si < session.Loaded.Count; si++)
                    {
                        IReadOnlyList<Entity> entities = session.Loaded[si].Scene.Entities;
                        for (int i = 0; i < entities.Count; i++)
                        {
                            Transform t = entities[i].Transform;
                            float[] v = [t.Position.X, t.Position.Y, t.Position.Z, t.Rotation.X, t.Rotation.Y, t.Rotation.Z,
                                t.Rotation.W, t.Scale.X, t.Scale.Y, t.Scale.Z];
                            lines.Add($"entity {si * 1000 + i} " + string.Join(" ", v.Select(F)));
                        }
                    }
                    lines.Add($"time {F(session.Time)} {F(session.DeltaTime)} {session.FrameCount}");
                    lines.Add("end");
                    break;
            }
        }
        return lines;
    }
}

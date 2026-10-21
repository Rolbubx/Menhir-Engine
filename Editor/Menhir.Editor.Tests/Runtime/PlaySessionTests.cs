using System.Numerics;
using System.Text.Json;
using Menhir.Editor.Build;
using Menhir.Editor.Input;
using Menhir.Editor.Logging;
using Menhir.Editor.Projects;
using Menhir.Editor.Runtime;
using Menhir.Editor.Scenes;
using Menhir.Editor.Scripting;

namespace Menhir.Editor.Tests.Runtime;

/// <summary>Scripts compiled from a project (dotnet build) and run in a play session, frame by frame.</summary>
public sealed class PlaySessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "menhir-tests", Guid.NewGuid().ToString("N"));
    private readonly List<(LogLevel Level, string Message)> _log = new();

    public PlaySessionTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string Scripts = """
        using MenhirEngine;
        using System.Collections.Generic;

        public enum Mode { Slow, Fast }

        public class Mover : Script
        {
            public float speed = 2f;
            public Vector3 direction = Vector3.right;
            public Mode mode;
            public string label = "mover";
            [HideInInspector] public int hidden;
            private List<string> _events = new List<string>();

            public override void Awake() => _events.Add("awake");
            public override void Start()
            {
                _events.Add("start");
                Debug.Log(label + " starts at " + transform.position.ToString("F1") + " in " + mode);
            }

            public override void Update()
            {
                float factor = mode == Mode.Fast ? 10f : 1f;
                transform.position += direction * (speed * factor * Time.deltaTime);
                if (Input.GetKeyDown("space"))
                    SceneManager.LoadScene("Second");
                if (Time.frameCount == 3)
                    Debug.Log(string.Join(",", _events) + " frame " + Time.frameCount + " " + Time.time.ToString("F2"));
            }
        }

        public class Follower : Script
        {
            public override void LateUpdate()
            {
                Entity target = Entity.Find("Player");
                if (target != null)
                    transform.position = target.transform.position + Vector3.up;
                if (target.GetComponent<Mover>() is Mover m && Time.frameCount == 1)
                    Debug.Log("following " + m.label);
            }
        }

        public class Broken : Script
        {
            public override void Update()
            {
                if (Time.frameCount == 2)
                    throw new System.InvalidOperationException("boom");
            }
        }
        """;

    private (Project Project, ScriptAssembly Scripts) CompileProject()
    {
        Project project = Project.Create(_root, "Game");
        Directory.CreateDirectory(Path.Combine(project.RootPath, "Assets", "Scripts"));
        File.WriteAllText(Path.Combine(project.RootPath, "Assets", "Scripts", "Game.cs"), Scripts);
        ScriptCompileResult result = ScriptCompiler.CompileForEditor(project, typeof(MenhirEngine.Script).Assembly.Location);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics) + result.Output);
        return (project, ScriptAssembly.Load(result.AssemblyPath!));
    }

    private static Scene First()
    {
        var scene = new Scene();
        Entity player = scene.CreateEntity("Player");
        player.Transform.Position = new Vector3(1, 0, 0);
        player.Components.Add(new ScriptComponent
        {
            ClassName = "Mover",
            Fields = new Dictionary<string, JsonElement>
            {
                ["speed"] = JsonSerializer.SerializeToElement(3f),
                ["direction"] = JsonSerializer.SerializeToElement(new[] { 0f, 0f, -1f }),
                ["mode"] = JsonSerializer.SerializeToElement("Fast"),
                ["label"] = JsonSerializer.SerializeToElement("hero"),
            },
        });
        Entity follower = scene.CreateEntity("Camera");
        follower.Components.Add(new ScriptComponent { ClassName = "Follower" });
        scene.CreateEntity("Bad").Components.Add(new ScriptComponent { ClassName = "Broken" });
        scene.CreateEntity("Ghost").Components.Add(new ScriptComponent { ClassName = "Missing" });
        return scene;
    }

    [Fact]
    public void Scripts_RunWithTheirSavedFields_InUnitysOrder()
    {
        var (_, scripts) = CompileProject();
        using (scripts)
        {
            Assert.Equal(["Broken", "Follower", "Mover"], scripts.ScriptTypes.Select(t => t.Name));
            Assert.Equal(["speed", "direction", "mode", "label"], ScriptFields.Of(scripts.Find("Mover")!).Select(f => f.Name));

            BuildScene[] build = [new("First", First()), new("Second", new Scene())];
            using var session = new PlaySession(build, 0, new InputSettings(), scripts, (level, message) => _log.Add((level, message)));
            for (int i = 0; i < 3; i++)
                session.Update(new InputFrame(), 0.5f);

            Entity player = session.Scenes[0].Entities[0];
            // 3 frames of 0.5 s at 3 m/s × 10 (Fast) along -Z.
            Assert.Equal(new Vector3(1, 0, -45), player.Transform.Position);
            Assert.Equal(new Vector3(1, 1, -45), session.Scenes[0].Entities[1].Transform.Position);
            Assert.Contains((LogLevel.Info, "hero starts at (1.0, 0.0, 0.0) in Fast"), _log);
            Assert.Contains((LogLevel.Info, "following hero"), _log);
            Assert.Contains((LogLevel.Info, "awake,start frame 3 1.50"), _log);
            Assert.Contains(_log, l => l.Level == LogLevel.Error && l.Message.Contains("Broken.Update") && l.Message.Contains("boom"));
            Assert.Contains(_log, l => l.Level == LogLevel.Warning && l.Message.Contains("Missing"));

            // The editor's scene is not touched.
            Assert.Equal(new Vector3(1, 0, 0), build[0].Scene.Entities[0].Transform.Position);

            var space = new InputFrame();
            space.Down.Add(32);
            session.Update(space, 0.1f);
            Assert.Equal(["Second"], session.Loaded.Select(s => s.Name));
        }
    }
}

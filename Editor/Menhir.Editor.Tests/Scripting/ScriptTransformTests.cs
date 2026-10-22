using Menhir.Editor.Build;
using Menhir.Editor.Projects;
using Menhir.Editor.Scripting;

namespace Menhir.Editor.Tests.Scripting;

/// <summary>The simple script syntax (MH_VARS, MH_START() { ... }): what the inspector sees, and the compiler's messages.</summary>
public sealed class ScriptTransformTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "menhir-tests", Guid.NewGuid().ToString("N"));
    private static readonly string Engine = PlayerBuilder.FindEngineDirectory(AppContext.BaseDirectory)!;

    public ScriptTransformTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Project ProjectWith(string file, string source)
    {
        Project project = Project.Create(_root, "Simple");
        File.WriteAllText(Path.Combine(project.Assets.AssetsDirectory, file), source);
        return project;
    }

    private ScriptCompileResult Compile(Project project) =>
        ScriptCompiler.CompileLibrary(project, Engine, Path.Combine(_root, "lib" + ScriptCompiler.LibraryExtension), noAudio: true);

    [Theory]
    [InlineData("MH_START() { }", true)]
    [InlineData("MH_VARS { int a; };", true)]
    [InlineData("MH_CREATE Player.c", true)]
    [InlineData("MH_FIXED_UPDATE(dt) { }", true)]
    [InlineData("int helper(void) { return 1; }", false)]
    [InlineData("typedef struct { int n; } Old;\nstatic void Old_start(Old *s, mh_entity e) {}\nMH_SCRIPT(Old, MH_START(Old_start));", false)]
    [InlineData("/* MH_START() { } */ int x;", false)]
    public void TheSimpleSyntax_IsRecognized(string source, bool simple) => Assert.Equal(simple, ScriptTransform.IsSimple(source));

    [Fact]
    public void MH_VARS_BecomesTheInspectorsFields()
    {
        Project project = ProjectWith("Hero.c", """
            MH_VARS {
                float speed = 90;        // MH_RANGE(0, 360) Degrees per second
                MH_VEC3 axis = {0, 1, 0};
                MH_COLOR tint = MH_COLOR(1, 0.5, 0.25, 1);
                MH_STRING title = "hero";
                bool flying;
                int hidden = 4;          // MH_HIDE
                MH_GAMEOBJECT target;
                float history[4];
            };

            MH_UPDATE() { self.speed += 0; }
            """);
        ScriptCompileResult result = Compile(project);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        ScriptType hero = ScriptLibrary.ReadTypes(result.LibraryPath!).Single(t => t.Name == "Hero");

        Assert.Equal(["speed", "axis", "tint", "title", "flying"], hero.Fields.Select(f => f.Name));
        ScriptField speed = hero.Fields[0];
        Assert.Equal((ScriptFieldKind.Float, 0f, 360f, "Degrees per second"), (speed.Kind, speed.Min!.Value, speed.Max!.Value, speed.Tooltip));
        Assert.Equal(90f, ScriptFields.DefaultValue(speed));
        Assert.Equal(new System.Numerics.Vector3(0, 1, 0), ScriptFields.DefaultValue(hero.Fields[1]));
        Assert.Equal(new System.Numerics.Vector4(1, 0.5f, 0.25f, 1), ScriptFields.DefaultValue(hero.Fields[2]));
        Assert.Equal("hero", ScriptFields.DefaultValue(hero.Fields[3]));
        Assert.Equal((ScriptFieldKind.Bool, false), (hero.Fields[4].Kind, ScriptFields.DefaultValue(hero.Fields[4])));
    }

    [Fact]
    public void CompileErrors_PointAtTheScriptsOwnLines()
    {
        Project project = ProjectWith("Broken.c", """
            MH_VARS {
                float speed = 2;
            };

            MH_UPDATE() {
                self.speed += nope;
            }
            """);
        ScriptCompileResult result = Compile(project);
        Assert.False(result.Success);
        ScriptDiagnostic error = result.Errors.First();
        Assert.Equal(("Assets/Broken.c", 6), (error.File, error.Line));
        Assert.Contains("nope", error.Message);
    }

    [Fact]
    public void MH_CREATE_HasToNameTheFile()
    {
        Project project = ProjectWith("Player.c", "MH_CREATE Enemy.c\n\nMH_START() { }\n");
        ScriptCompileResult result = Compile(project);
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.File == "Assets/Player.c" && e.Line == 1 && e.Message.Contains("MH_CREATE names Enemy"));
    }

    [Fact]
    public void ABadDefault_IsReportedOnItsLine()
    {
        Project project = ProjectWith("Mover.c", "MH_VARS {\n    float speed = 1;\n    MH_VEC3 axis = 5;\n};\nMH_UPDATE() { }\n");
        ScriptCompileResult result = Compile(project);
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Line == 3 && e.Message.Contains("has 3 numbers"));
    }

    [Fact]
    public void AScriptWithoutVariablesOrCallbacksItDoesNotUse_StillCompiles()
    {
        Project project = ProjectWith("Quiet.c", "MH_CREATE(Quiet)\nMH_TIMER(never) { }\n");
        ScriptCompileResult result = Compile(project);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.Contains(ScriptLibrary.ReadTypes(result.LibraryPath!), t => t.Name == "Quiet" && t.Fields.Count == 0);
    }
}

/// <summary>Sounds in Play mode, with miniaudio linked (it is compiled once into the user's cache).</summary>
public sealed class AudioTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "menhir-tests", Guid.NewGuid().ToString("N"));
    private static readonly string Engine = PlayerBuilder.FindEngineDirectory(AppContext.BaseDirectory)!;

    public AudioTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A short mono 16-bit WAV.</summary>
    private static byte[] Beep()
    {
        const int rate = 22050, samples = rate / 10;
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples * 2);
        for (int i = 0; i < samples; i++)
            w.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 8000));
        return stream.ToArray();
    }

    [Fact]
    public void Sounds_PlayOrStaySilent_WithoutErrors()
    {
        Project project = Project.Create(_root, "Noisy");
        Directory.CreateDirectory(Path.Combine(project.Assets.AssetsDirectory, "Sounds"));
        File.WriteAllBytes(Path.Combine(project.Assets.AssetsDirectory, "Sounds", "beep.wav"), Beep());
        File.WriteAllText(Path.Combine(project.Assets.AssetsDirectory, "Jukebox.c"), """
            MH_START() {
                MH_PLAY_SOUND("Sounds/beep.wav");
                MH_PLAY(MH_PARENT);
                MH_LOG("playing %d", MH_IS_PLAYING(MH_PARENT));
            }
            """);
        ScriptCompileResult result = ScriptCompiler.CompileLibrary(project, Engine, Path.Combine(_root, "lib" + ScriptCompiler.LibraryExtension));
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        using ScriptLibrary library = ScriptLibrary.Load(result.LibraryPath!);

        var scene = new Menhir.Editor.Scenes.Scene();
        Menhir.Editor.Scenes.Entity box = scene.CreateEntity("Jukebox");
        box.Components.Add(new Menhir.Editor.Scenes.ScriptComponent
        {
            ScriptName = "AudioSource",
            Fields = new() { ["clip"] = System.Text.Json.JsonSerializer.SerializeToElement("Sounds/beep.wav") },
        });
        box.Components.Add(new Menhir.Editor.Scenes.ScriptComponent { ScriptName = "Jukebox" });
        var lines = new List<string>();
        using (var session = new Menhir.Editor.Runtime.PlaySession([new BuildScene("Main", scene)], 0, new Menhir.Editor.Input.InputSettings(), library,
                   (level, message) => lines.Add($"{level} {message}"), project.Assets.AssetsDirectory))
        {
            for (int i = 0; i < 5; i++)
                session.Update(new Menhir.Editor.Input.InputFrame(), 0.02f);
        }
        // With a device the sound plays; without one, a single warning says so.
        Assert.DoesNotContain(lines, l => l.StartsWith("Error", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("Info playing", StringComparison.Ordinal));
    }
}

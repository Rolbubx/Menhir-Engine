using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Menhir.Editor.Build;
using Menhir.Editor.Input;
using Menhir.Editor.Projects;
using Menhir.Editor.Runtime;
using Menhir.Editor.Scenes;
using Menhir.Editor.Scripting;

namespace Menhir.Editor.Tests.Build;

/// <summary>
/// Differential tests of scripts: the same project's C scripts run in the editor's Play mode (compiled
/// into a library the editor loads) and in the built game (compiled with the player, without a window,
/// playing a script of input events with --simulate). The log must be the same, word for word, and the
/// entities' transforms the same within float rounding.
/// </summary>
public sealed class PlayerScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "menhir-tests", Guid.NewGuid().ToString("N"));
    private static readonly string Engine = PlayerBuilder.FindEngineDirectory(AppContext.BaseDirectory)!;

    public PlayerScriptTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string Mover = """
        #include <menhir.h>
        #include "Shared.h"

        static const mh_field Mover_fields[] = {
            MH_INT(Mover, count, 3),
            MH_FLOAT(Mover, speed, 2, MH_RANGE(0, 10), MH_TOOLTIP("Meters per second")),
            MH_BOOL(Mover, active, true),
            MH_STRING(Mover, label, "mover"),
            MH_ENUM(Mover, mode, 0, MH_OPTIONS("Slow", "Fast")),
            MH_VEC2(Mover, offset, 1, 1),
            MH_VEC3(Mover, direction, 1, 0, 0),
            MH_COLOR(Mover, tint, 1, 0.5, 0.25, 1),
            MH_INT(Mover, broken, 4),
        };

        static void Mover_awake(Mover *self, mh_entity e)
        {
            self->events = 1;
            mh_log("%s awake %d %g %d %s %d (%.2f, %.2f) (%.2f, %.2f, %.2f) (%.2f, %.2f, %.2f, %.2f) %d", mh_get_name(e),
                   self->count, self->speed, self->active, self->label, self->mode, self->offset.x, self->offset.y,
                   self->direction.x, self->direction.y, self->direction.z, self->tint.r, self->tint.g, self->tint.b,
                   self->tint.a, self->broken);
        }

        static void Mover_start(Mover *self, mh_entity e)
        {
            self->events |= 2;
            mh_vec3 p = mh_get_position(e);
            mh_log("%s starts at (%.3f, %.3f, %.3f) on frame %d", self->label, p.x, p.y, p.z, mh_frame_count());
        }

        static void Mover_update(Mover *self, mh_entity e)
        {
            float factor = self->mode == 1 ? 2.0f : 1.0f;
            mh_vec3 move = mh_v3_scale(self->direction, self->speed * factor * mh_delta_time());
            move.x += mh_axis("Horizontal") * mh_delta_time();
            mh_translate(e, move, true);
            mh_rotate(e, mh_v3(0, 30 * mh_delta_time(), 0), false);
            if (mh_key_down("space")) {
                mh_vec2 mouse = mh_mouse_position();
                mh_log("space on frame %d, mouse at (%.1f, %.1f), events %d", mh_frame_count(), mouse.x, mouse.y, self->events);
            }
            if (mh_button_down("Fire1"))
                mh_load_scene("Second", MH_LOAD_ADDITIVE);
            if (mh_key_down("q"))
                mh_quit();
            if (mh_key_down("nonsense"))
                mh_log("never");
        }

        MH_SCRIPT(Mover, MH_FIELDS(Mover_fields), MH_AWAKE(Mover_awake), MH_START(Mover_start), MH_UPDATE(Mover_update));
        """;

    private const string Others = """
        #include <menhir.h>
        #include "Shared.h"

        /* A child that looks at a point near the player and reports where it is. */
        typedef struct { float distance; } Follower;
        static const mh_field Follower_fields[] = { MH_FLOAT(Follower, distance, 2) };

        static void Follower_late_update(Follower *self, mh_entity e)
        {
            mh_entity player = mh_find("Player");
            const Mover *mover = mh_get_script(player, "Mover");
            mh_look_at(e, mh_v3_add(mh_get_position(player), mh_v3(0, 0, -self->distance)));
            if (mh_frame_count() % 5 == 0) {
                mh_vec3 p = mh_get_position(e), euler = mh_quat_to_euler(mh_get_rotation(e));
                mh_log("follower of %s at (%.2f, %.2f, %.2f) parent %s rotation (%.1f, %.1f, %.1f) %s", mover ? mover->label : "?",
                       p.x, p.y, p.z, mh_get_name(mh_get_parent(e)), euler.x, euler.y, euler.z, shared_greeting());
            }
        }

        MH_SCRIPT(Follower, MH_FIELDS(Follower_fields), MH_LATE_UPDATE(Follower_late_update));

        /* No fields at all. */
        typedef struct { int updates; } Dice;

        static void Dice_start(Dice *self, mh_entity e)
        {
            (void)self;
            mh_random_seed(42);
            int a = mh_random_int(1, 7), b = mh_random_int(1, 7), c = mh_random_int(1, 7);
            mh_log_warning("rolls %d %d %d %.4f on %s", a, b, c, mh_random_range(0, 1), mh_get_name(e));
        }

        static void Dice_update(Dice *self, mh_entity e)
        {
            (void)e;
            if (++self->updates == 2)
                mh_log_error("dice updated twice, %s", mh_get_name(4242));
        }

        MH_SCRIPT(Dice, MH_START(Dice_start), MH_UPDATE(Dice_update));

        typedef struct { char unused; } Spawned;
        static void Spawned_awake(Spawned *self, mh_entity e) { (void)self; (void)e; mh_log("spawned awake on frame %d", mh_frame_count()); }
        static void Spawned_start(Spawned *self, mh_entity e) { (void)self; (void)e; mh_log("spawned start on frame %d", mh_frame_count()); }
        MH_SCRIPT(Spawned, MH_AWAKE(Spawned_awake), MH_START(Spawned_start));
        """;

    private const string SharedHeader = """
        #include <menhir.h>

        typedef struct {
            int32_t count;
            float speed;
            bool active;
            const char *label;
            int32_t mode;
            mh_vec2 offset;
            mh_vec3 direction;
            mh_color tint;
            int32_t broken;
            int events;
        } Mover;

        static inline const char *shared_greeting(void) { return "hello"; }
        """;

    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value);

    private static (Scene Main, Scene Second) Scenes()
    {
        var main = new Scene();
        Entity player = main.CreateEntity("Player");
        player.Transform.Position = new Vector3(1, 0, 2);
        player.Components.Add(new ScriptComponent
        {
            ScriptName = "Mover",
            Fields = new()
            {
                ["count"] = J(5), ["speed"] = J(1.5f), ["active"] = J(false), ["label"] = J("hero"), ["mode"] = J("Fast"),
                ["offset"] = J(new[] { 0.5f, -2f }), ["direction"] = J(new[] { 0f, 0f, -1f }),
                ["tint"] = J(new[] { 0.1f, 0.2f, 0.3f, 0.4f }), ["broken"] = J("oops"), ["gone"] = J(1),
            },
        });
        Entity child = main.CreateEntity("Child");
        child.ParentId = player.LocalId;
        child.Transform.Position = new Vector3(0, 1, 0);
        child.Components.Add(new ScriptComponent { ScriptName = "Follower", Fields = new() { ["distance"] = J(3) } });
        Entity thrower = main.CreateEntity("Dice");
        thrower.Components.Add(new ScriptComponent { ScriptName = "Dice" });
        thrower.Components.Add(new ScriptComponent { ScriptName = "Missing" });

        var second = new Scene();
        second.CreateEntity("Extra").Components.Add(new ScriptComponent { ScriptName = "Spawned" });
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
        down 81          # q: mh_quit
        frame 0.02 2
        dump
        """;

    private Project CreateProject()
    {
        Project project = Project.Create(_root, "Game");
        string scripts = Path.Combine(project.Assets.AssetsDirectory, "Scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "Mover.c"), Mover);
        File.WriteAllText(Path.Combine(scripts, "Others.c"), Others);
        File.WriteAllText(Path.Combine(scripts, "Shared.h"), SharedHeader);
        // No #include: the compiler adds menhir.h to every script.
        File.WriteAllText(Path.Combine(scripts, "Bare.c"), "typedef struct { int n; } Bare;\nMH_SCRIPT(Bare);\n");
        return project;
    }

    [Fact]
    public void Scripts_RunTheSameInTheBuiltGameAsInPlayMode()
    {
        Project project = CreateProject();
        (Scene main, Scene second) = Scenes();
        BuildScene[] scenes = [new("Main", main), new("Second", second)];

        List<string> native = Native(project, scenes);
        List<string> editor = Editor(project, scenes);

        Assert.Contains("quit", native);
        Assert.Contains("log 1 Dice: the script Missing does not exist.", native);
        Assert.Contains("log 1 Player: the saved value of Mover.broken does not fit the field; it keeps its default.", native);
        Assert.Contains("log 0 Player awake 5 1.5 0 hero 1 (0.50, -2.00) (0.00, 0.00, -1.00) (0.10, 0.20, 0.30, 0.40) 4", native);
        Assert.Contains("log 2 There is no key named 'nonsense'.", native);
        Assert.Contains(native, l => l.StartsWith("log 0 follower of hero at", StringComparison.Ordinal) && l.EndsWith(" hello", StringComparison.Ordinal));
        Assert.Contains(native, l => l.StartsWith("log 1 rolls ", StringComparison.Ordinal));
        Assert.Contains("log 0 spawned start on frame 6", native);

        string Show(List<string> lines) => string.Join("\n", lines);
        Assert.True(native.Count == editor.Count, $"The game printed:\n{Show(native)}\n\nPlay mode printed:\n{Show(editor)}");
        for (int i = 0; i < native.Count; i++)
            Assert.True(Same(editor[i], native[i]), $"Line {i}: Play mode printed\n  {editor[i]}\nthe game printed\n  {native[i]}");
    }

    // ---- The simple syntax, objects, tweens, timers, coroutines and physics -------------------------

    private const string Spinner = """
        MH_CREATE Spinner.c

        MH_VARS {
            float speed = 90;           // MH_RANGE(0, 360) Degrees per second
            MH_VEC3 axis = MH_VEC3(0, 1, 0);
            MH_STRING title = "spinner";
            int lives = 3;
            MH_GAMEOBJECT player;
            int ticks;                  // MH_HIDE
            int steps;                  // MH_HIDE
            float glow = 0.5f;          // MH_HIDE
        };

        MH_TIMER(tick) { self.ticks++; }

        MH_TIMER(turned)
        {
            mh_vec3 e = mh_quat_to_euler(mh_get_rotation(MH_PARENT));
            MH_LOG("turned to %.1f on frame %d", e.y, mh_frame_count());
            MH_MOVE(MH_PARENT, MH_VEC3(0, 0, -2), 0.3f, MH_TWEEN_TYPE(MH_EASE_OUT));
        }

        MH_COROUTINE(sequence)
        {
            MH_LOG("sequence begins, glow %.2f", self.glow);
            MH_WAIT(0.25f);
            MH_LOG("sequence waited, ticks %d", self.ticks);
            MH_WAIT_FRAME();
            MH_GAMEOBJECT copy = MH_SPAWN(MH_SCENE.Crate, MH_VEC3(3, 5, 0));
            MH_LOG("spawned %s with %d child, lid %s", MH_NAME(copy), MH_CHILD_COUNT(copy), MH_NAME(MH_FIND_CHILD(copy, "Lid")));
            MH_WAIT(0.1f);
            MH_SET_ACTIVE(MH_SCENE.Lamp, false);
            MH_DESTROY(MH_SCENE.Old_Crate);
            MH_LOG("lamp active %d, missing %d", MH_IS_ACTIVE(MH_SCENE.Lamp), MH_IS_NULL(MH_SCENE.Nothing_Here));
            MH_WAIT_UNTIL(MH_IS_NULL(MH_SCENE.Old_Crate));
            MH_LOG("old crate gone on frame %d", mh_frame_count());
        }

        MH_START()
        {
            MH_GAMEOBJECT player = MH_PARENT;
            MH_REFERENCE *pplayer = &player;
            self.player = player;
            MH_LOG("start %s %s speed %.0f lives %d axis %.0f %.0f %.0f", MH_NAME(pplayer), self.title, self.speed, self.lives,
                   self.axis.x, self.axis.y, self.axis.z);
            MH_TWEEN turn = MH_ROTATE(pplayer, MH_VEC3(0, 90, 0), 0.5f, MH_TWEEN_TYPE(MH_LINEAR));
            MH_ON_COMPLETE(turn, turned);
            MH_EVERY(0.1f, tick);
            MH_START_COROUTINE(sequence);
        }

        MH_FIXED_UPDATE(dt)
        {
            self.steps++;
            (void)dt;
        }

        MH_UPDATE()
        {
            MH_SPIN(MH_SCENE.Lamp, MH_VEC3(0, self.speed, 0));
            if (MH_KEY_DOWN("space"))
                MH_LOG("space: %d steps, %d ticks", self.steps, self.ticks);
        }

        MH_ON_GAME_CLOSE(t)
        {
            MH_LOG("closing after %.2f s and %d fixed steps", t, self.steps);
        }
        """;

    private const string Ball = """
        MH_VARS {
            int bounces;
        };

        MH_ON_COLLISION_ENTER(other)
        {
            self.bounces++;
            if (self.bounces <= 3)
                MH_LOG("%s hits %s (%d) at %.2f", MH_NAME(MH_PARENT), MH_NAME(other), self.bounces, MH_GET_POSITION(MH_PARENT).y);
        }

        MH_ON_TRIGGER_ENTER(other) { MH_LOG("%s enters %s", MH_NAME(MH_PARENT), MH_NAME(other)); }
        MH_ON_TRIGGER_EXIT(other) { MH_LOG("%s leaves %s", MH_NAME(MH_PARENT), MH_NAME(other)); }

        MH_UPDATE()
        {
            MH_HIT hit;
            if (mh_frame_count() == 30 && MH_RAYCAST(MH_VEC3(0, 10, 0), MH_VEC3(0, -1, 0), 50, &hit))
                MH_LOG("ray hits %s at %.2f", MH_NAME(hit.entity), hit.distance);
        }

        MH_ON_DESTROY() { MH_LOG("%s destroyed", MH_NAME(MH_PARENT)); }
        """;

    private const string ArcadeInput = """
        frame 0.02 40
        dump
        frame 0.016 25
        down 32
        frame 0.016
        up 32
        dump
        """;

    private static Scene ArcadeScene()
    {
        var scene = new Scene();
        Entity Add(string name, Vector3 position, params Component[] components)
        {
            Entity e = scene.CreateEntity(name);
            e.Transform.Position = position;
            e.Components.AddRange(components);
            return e;
        }
        ScriptComponent Script(string name, Dictionary<string, JsonElement>? fields = null) => new() { ScriptName = name, Fields = fields ?? new() };

        Add("Player", Vector3.Zero, Script("Spinner", new() { ["lives"] = J(5) }));
        Add("Floor", Vector3.Zero, Script("BoxCollider", new() { ["size"] = J(new[] { 10f, 1f, 10f }) }));
        Add("Ball", new Vector3(0, 3, 0), Script("SphereCollider"), Script("Rigidbody", new() { ["bounciness"] = J(0.5f) }), Script("Ball"));
        Add("Zone", new Vector3(0, 1.75f, 0), Script("BoxCollider", new() { ["size"] = J(new[] { 2f, 0.5f, 2f }), ["is_trigger"] = J(true) }));
        Entity crate = Add("Crate", new Vector3(-3, 0, 0));
        scene.CreateEntity("Lid", crate.LocalId).Transform.Position = new Vector3(0, 1, 0);
        Add("Lamp", new Vector3(0, 4, 0));
        Add("Old Crate", new Vector3(5, 0, 0), Script("Ball"));
        return scene;
    }

    [Fact]
    public void SimpleScripts_Physics_Tweens_AndObjects_RunTheSameInBoth()
    {
        Project project = Project.Create(_root, "Arcade");
        string scripts = Path.Combine(project.Assets.AssetsDirectory, "Scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "Spinner.c"), Spinner);
        File.WriteAllText(Path.Combine(scripts, "Ball.c"), Ball);
        BuildScene[] scenes = [new("Arcade", ArcadeScene())];

        List<string> native = Native(project, scenes, ArcadeInput);
        List<string> editor = Editor(project, scenes, ArcadeInput);

        string Show(List<string> lines) => string.Join("\n", lines);
        Assert.Contains("log 0 start Player spinner speed 90 lives 5 axis 0 1 0", native);
        Assert.Contains("log 0 sequence begins, glow 0.50", native);
        Assert.Contains(native, l => l.StartsWith("log 0 turned to 90.0 on frame", StringComparison.Ordinal));
        Assert.Contains("log 0 spawned Crate with 1 child, lid Lid", native);
        Assert.Contains("log 0 lamp active 0, missing 1", native);
        Assert.Contains("log 0 Old Crate destroyed", native);
        Assert.Contains(native, l => l.StartsWith("log 0 old crate gone on frame", StringComparison.Ordinal));
        Assert.Contains(native, l => l.StartsWith("log 0 Ball hits Floor (1)", StringComparison.Ordinal));
        Assert.Contains("log 0 Ball enters Zone", native);
        Assert.Contains("log 0 Ball leaves Zone", native);
        Assert.Contains(native, l => l.StartsWith("log 0 ray hits Ball", StringComparison.Ordinal));
        Assert.Contains(native, l => l.StartsWith("log 0 space: ", StringComparison.Ordinal));
        Assert.Contains(native, l => l.StartsWith("log 0 closing after ", StringComparison.Ordinal));
        Assert.Contains("log 0 Ball destroyed", native);
        Assert.True(native.Count == editor.Count, $"The game printed:\n{Show(native)}\n\nPlay mode printed:\n{Show(editor)}");
        for (int i = 0; i < native.Count; i++)
            Assert.True(Same(editor[i], native[i]), $"Line {i}: Play mode printed\n  {editor[i]}\nthe game printed\n  {native[i]}\n\n{Show(native)}");
    }

    [Fact]
    public void Library_DescribesTheScriptsFields()
    {
        Project project = CreateProject();
        ScriptCompileResult compiled = ScriptCompiler.CompileLibrary(project, Engine, Path.Combine(_root, "lib" + ScriptCompiler.LibraryExtension));
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        IReadOnlyList<ScriptType> types = ScriptLibrary.ReadTypes(compiled.LibraryPath!);

        Assert.Equal(["AudioSource", "Bare", "BoxCollider", "Dice", "Follower", "Mover", "Rigidbody", "Spawned", "SphereCollider"],
            types.Select(t => t.Name));
        ScriptType mover = types.Single(t => t.Name == "Mover");
        Assert.Equal(["count", "speed", "active", "label", "mode", "offset", "direction", "tint", "broken"], mover.Fields.Select(f => f.Name));
        ScriptField speed = mover.Fields[1];
        Assert.Equal((ScriptFieldKind.Float, 0f, 10f, "Meters per second"), (speed.Kind, speed.Min!.Value, speed.Max!.Value, speed.Tooltip));
        Assert.Equal(2f, ScriptFields.DefaultValue(speed));
        Assert.Equal("mover", ScriptFields.DefaultValue(mover.Fields[3]));
        Assert.Equal(["Slow", "Fast"], mover.Fields[4].Options);
        Assert.Equal(new Vector4(1, 0.5f, 0.25f, 1), ScriptFields.DefaultValue(mover.Fields[7]));
        Assert.Equal(J("Fast").GetRawText(), ScriptFields.ToJson(mover.Fields[4], 1).GetRawText());
        Assert.Equal(1, ScriptFields.FromJson(mover.Fields[4], J("Fast")));
        Assert.Null(ScriptFields.FromJson(mover.Fields[4], J("Medium")));
        Assert.Null(ScriptFields.FromJson(mover.Fields[0], J(1.5)));
        Assert.Empty(types.Single(t => t.Name == "Dice").Fields);
    }

    [Fact]
    public void CompileErrors_PointAtTheScript()
    {
        Project project = Project.Create(_root, "Broken");
        File.WriteAllText(Path.Combine(project.Assets.AssetsDirectory, "Bad.c"), "#include <menhir.h>\nint f(void) { return nope; }\n");
        ScriptCompileResult result = ScriptCompiler.CompileLibrary(project, Engine, Path.Combine(_root, "bad" + ScriptCompiler.LibraryExtension));
        Assert.False(result.Success);
        ScriptDiagnostic error = result.Errors.First();
        Assert.Equal(("Assets/Bad.c", 2), (error.File, error.Line));
        Assert.Contains("nope", error.Message);
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

    private List<string> Native(Project project, BuildScene[] scenes, string inputScript = Input)
    {
        string data = Path.Combine(_root, "GameData.binchunk");
        var warnings = new List<string>();
        File.WriteAllBytes(data, GameDataExporter.Export("Test", scenes, new RenderingSettings(), new BuildSettings(),
            _ => null, _ => throw new IOException(), warnings, new InputSettings()));
        Assert.Empty(warnings);
        string mainC = Path.Combine(_root, "main.c");
        File.WriteAllText(mainC, PlayerSource.Amalgamate(Engine));
        string executable = Path.Combine(_root, "player");
        (List<string> scripts, List<ScriptDiagnostic> prepared) = ScriptCompiler.PrepareSources(project, Path.Combine(_root, "Generated"));
        Assert.Empty(prepared);
        new PlayerBuilder(Engine).BuildHeadless(mainC, executable, [.. scripts, .. ScriptCompiler.ApiSources(Engine)]);
        string input = Path.Combine(_root, "input.txt");
        File.WriteAllText(input, inputScript);

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
    private List<string> Editor(Project project, BuildScene[] scenes, string inputScript = Input)
    {
        ScriptCompileResult compiled = ScriptCompiler.CompileLibrary(project, Engine, Path.Combine(_root, "Library", "GameScripts" + ScriptCompiler.LibraryExtension),
            noAudio: true);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        using ScriptLibrary library = ScriptLibrary.Load(compiled.LibraryPath!);

        var lines = new List<string>();
        using var session = new PlaySession(scenes, 0, new InputSettings(), library,
            (level, message) => lines.Add($"log {(int)level} {message}"));
        var frame = new InputFrame();
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        foreach (string raw in inputScript.Split('\n'))
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

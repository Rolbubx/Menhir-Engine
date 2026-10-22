using System.Numerics;
using System.Runtime.InteropServices;
using Menhir.Editor.Build;
using Menhir.Editor.Input;
using Menhir.Editor.Logging;
using Menhir.Editor.Scenes;
using Menhir.Editor.Scripting;
using Menhir.Editor.Serialization;

namespace Menhir.Editor.Runtime;

/// <summary>A scene loaded in a play session: a copy of the build's scene, and what runs on it.</summary>
public sealed class LoadedScene
{
    internal LoadedScene(int serial, int index, string name, Scene scene)
    {
        Serial = serial;
        Index = index;
        Name = name;
        Scene = scene;
    }

    /// <summary>Unique per load: entity handles carry it, so handles of unloaded scenes go stale.</summary>
    public int Serial { get; }
    /// <summary>The scene's index in the build.</summary>
    public int Index { get; }
    public string Name { get; }
    public Scene Scene { get; }

    internal List<RunningScript> Scripts { get; } = new();
    internal nint[] Names { get; set; } = []; // entity names as UTF-8, for mh_get_name
    internal List<FlyCameraBehaviour> FlyCameras { get; } = new();
    internal List<(SceneSwitcher Switcher, int Target)> Switchers { get; } = new();
}

/// <summary>A script of a loaded scene: its type and its struct (native memory).</summary>
internal sealed unsafe class RunningScript(int entity, ScriptType type, void* memory)
{
    public int Entity { get; } = entity;
    public ScriptType Type { get; } = type;
    public void* Memory { get; } = memory;
    public bool Started { get; set; }
}

/// <summary>
/// The game running in the editor (Play mode): copies of the build's scenes, the Input Manager, the
/// built-in behaviours and the project's C scripts (a <see cref="ScriptLibrary"/>), which reach the
/// engine through <see cref="ScriptHost"/>. Each <see cref="Update"/> is one frame, in the same order as
/// the built game's player (mh_world.c): input, Start (scripts not started yet), Update, built-in
/// behaviours, LateUpdate, then the scene change asked for during the frame. A script that crashes
/// takes the editor down with it: they run in its process.
/// </summary>
public sealed unsafe class PlaySession : IDisposable
{
    private delegate* unmanaged<void*, int, void> Callback(nint pointer) => (delegate* unmanaged<void*, int, void>)pointer;

    private readonly IReadOnlyList<BuildScene> _buildScenes;
    private readonly ScriptLibrary? _scripts;
    private readonly Action<LogLevel, string> _log;
    private readonly List<LoadedScene> _loaded = new();
    private readonly List<nint> _strings = new(); // native strings given to scripts, freed at the end
    private int _nextSerial = 1;
    private (int Index, LoadSceneMode Mode)? _request;

    /// <param name="buildScenes">The scenes the game can load, by build index.</param>
    /// <param name="scripts">The compiled scripts (null: the project has none). The session uses it until disposed.</param>
    /// <param name="log">mh_log and the warnings about scripts.</param>
    public PlaySession(IReadOnlyList<BuildScene> buildScenes, int firstScene, InputSettings input, ScriptLibrary? scripts,
        Action<LogLevel, string> log)
    {
        _buildScenes = buildScenes;
        _scripts = scripts;
        _log = log;
        Input = new InputState(input.Axes);
        ScriptHost.Current = this;
        _scripts?.Bind(ScriptHost.Api);
        Load(firstScene, LoadSceneMode.Single);
    }

    public InputState Input { get; }
    public IReadOnlyList<LoadedScene> Loaded => _loaded;
    public IReadOnlyList<Scene> Scenes => _loaded.Select(s => s.Scene).ToList();
    public float DeltaTime { get; private set; }
    public float Time { get; private set; }
    public int FrameCount { get; private set; }

    /// <summary>A fly camera is looking around (the editor captures the cursor).</summary>
    public bool CursorCaptured { get; private set; }

    /// <summary>mh_quit was called.</summary>
    public bool QuitRequested { get; private set; }

    public void Update(InputFrame frame, float deltaTime)
    {
        ScriptHost.Current = this;
        Input.Update(frame, deltaTime);
        DeltaTime = deltaTime;
        Time += deltaTime;
        FrameCount++;

        RunScripts(start: true, s => s.Type.Start);
        RunScripts(start: false, s => s.Type.Update);

        bool looking = false;
        foreach (LoadedScene scene in _loaded.ToList())
        {
            foreach (FlyCameraBehaviour fly in scene.FlyCameras)
            {
                fly.Update(Input, deltaTime);
                looking |= fly.IsLooking;
            }
            foreach ((SceneSwitcher switcher, int target) in scene.Switchers)
            {
                if (Input.GetButtonDown(switcher.Button))
                    _request = (target, switcher.Mode);
            }
        }
        CursorCaptured = looking;

        RunScripts(start: false, s => s.Type.LateUpdate);

        if (_request is { } request)
        {
            _request = null;
            Load(request.Index, request.Mode);
        }
    }

    /// <summary>Start runs once, before a script's first Update.</summary>
    private void RunScripts(bool start, Func<RunningScript, nint> callback)
    {
        foreach (LoadedScene scene in _loaded)
        {
            foreach (RunningScript script in scene.Scripts)
            {
                if (start == script.Started)
                    continue;
                script.Started = true;
                if (callback(script) is not 0 and var function)
                    Callback(function)(script.Memory, Handle(scene, script.Entity));
            }
        }
    }

    // ---- Scenes ---------------------------------------------------------------------------------

    private void Load(int index, LoadSceneMode mode)
    {
        if (index < 0 || index >= _buildScenes.Count)
            return;
        if (mode == LoadSceneMode.Single)
        {
            foreach (LoadedScene scene in _loaded)
                Unload(scene);
            _loaded.Clear();
        }
        else if (_loaded.Any(s => s.Index == index))
            return;

        // A fresh copy: the editor's scene stays as it is.
        BuildScene source = _buildScenes[index];
        Scene copy = SceneSerializer.Deserialize(SceneSerializer.Serialize(source.Scene), []);
        var loaded = new LoadedScene(_nextSerial++, index, source.Name, copy);
        _loaded.Add(loaded);

        IReadOnlyList<Entity> entities = copy.Entities;
        loaded.Names = entities.Select(e => Marshal.StringToCoTaskMemUTF8(e.Name)).ToArray();
        for (int i = 0; i < entities.Count; i++)
        {
            Entity entity = entities[i];
            foreach (Component component in entity.Components)
            {
                switch (component)
                {
                    case ScriptComponent sc when CreateScript(sc, entity, i) is { } script:
                        loaded.Scripts.Add(script);
                        break;
                    case FlyCamera fly when entity.Components.OfType<FlyCamera>().First() == fly:
                        loaded.FlyCameras.Add(new FlyCameraBehaviour(fly, entity.Transform));
                        break;
                    case SceneSwitcher switcher when entity.Components.OfType<SceneSwitcher>().First() == switcher:
                        int target = FindScene(switcher.Scene);
                        if (target >= 0)
                            loaded.Switchers.Add((switcher, target));
                        else
                            _log(LogLevel.Warning, $"The Scene Switcher of {entity.Name} loads '{switcher.Scene}', which is not in the build.");
                        break;
                }
            }
        }
        foreach (RunningScript script in loaded.Scripts.ToList())
        {
            if (script.Type.Awake != 0)
                Callback(script.Type.Awake)(script.Memory, Handle(loaded, script.Entity));
        }
    }

    private RunningScript? CreateScript(ScriptComponent component, Entity entity, int index)
    {
        if (_scripts?.Find(component.ScriptName) is not { } type)
        {
            _log(LogLevel.Warning, $"{entity.Name}: the script {component.ScriptName} does not exist.");
            return null;
        }
        void* memory = NativeMemory.AlignedAlloc((nuint)Math.Max(type.Size, 1), 16);
        NativeMemory.Clear(memory, (nuint)Math.Max(type.Size, 1));
        foreach (ScriptField field in type.Fields)
        {
            object value = ScriptFields.DefaultValue(field);
            if (component.Fields.TryGetValue(field.Name, out var json))
            {
                if (ScriptFields.FromJson(field, json) is { } saved)
                    value = saved;
                else
                    _log(LogLevel.Warning, $"{entity.Name}: the saved value of {component.ScriptName}.{field.Name} does not fit the field; it keeps its default.");
            }
            ScriptFields.Write(memory, field, value, NativeString);
        }
        return new RunningScript(index, type, memory);
    }

    private nint NativeString(string text)
    {
        nint pointer = Marshal.StringToCoTaskMemUTF8(text);
        _strings.Add(pointer);
        return pointer;
    }

    private static void Unload(LoadedScene scene)
    {
        foreach (RunningScript script in scene.Scripts)
            NativeMemory.AlignedFree(script.Memory);
        scene.Scripts.Clear();
        foreach (nint name in scene.Names)
            Marshal.FreeCoTaskMem(name);
        scene.Names = [];
    }

    private int FindScene(string name) =>
        _buildScenes.Select((s, i) => (s, i)).FirstOrDefault(x => x.s.Name == name.Trim(), (null!, -1)).Item2;

    // ---- Entity handles: (load serial << 16) | entity index --------------------------------------

    private static int Handle(LoadedScene scene, int index) => (scene.Serial & 0x7FFF) << 16 | index;

    private (LoadedScene Scene, int Index)? Resolve(int handle)
    {
        if (handle < 0)
            return null;
        int serial = handle >> 16, index = handle & 0xFFFF;
        LoadedScene? scene = _loaded.FirstOrDefault(s => (s.Serial & 0x7FFF) == serial);
        if (scene == null || index >= scene.Scene.Entities.Count)
            return null;
        return (scene, index);
    }

    private Scenes.Transform? TransformOf(int handle)
    {
        if (Resolve(handle) is var (scene, index))
            return scene.Scene.Entities[index].Transform;
        _log(LogLevel.Error, $"Entity {handle} is not in a loaded scene.");
        return null;
    }

    // ---- What ScriptHost forwards (the mh_api table) --------------------------------------------

    internal void GetTransform(int entity, int what, Span<float> output)
    {
        Scenes.Transform? t = TransformOf(entity);
        Vector4 v = what switch
        {
            0 => new Vector4(t?.Position ?? Vector3.Zero, 0),
            1 => t is { } r ? new Vector4(r.Rotation.X, r.Rotation.Y, r.Rotation.Z, r.Rotation.W) : new Vector4(0, 0, 0, 1),
            _ => new Vector4(t?.Scale ?? Vector3.One, 0),
        };
        output[0] = v.X;
        output[1] = v.Y;
        output[2] = v.Z;
        if (what == 1)
            output[3] = v.W;
    }

    internal void SetTransform(int entity, int what, ReadOnlySpan<float> values)
    {
        if (TransformOf(entity) is not { } t)
            return;
        if (what == 0)
            t.Position = new Vector3(values[0], values[1], values[2]);
        else if (what == 1)
            t.Rotation = new Quaternion(values[0], values[1], values[2], values[3]);
        else
            t.Scale = new Vector3(values[0], values[1], values[2]);
    }

    internal int GetParent(int entity)
    {
        if (Resolve(entity) is not var (scene, index))
            return -1;
        long parent = scene.Scene.Entities[index].ParentId;
        int found = parent == 0 ? -1 : scene.Scene.Entities.ToList().FindIndex(e => e.LocalId == parent);
        return found < 0 ? -1 : Handle(scene, found);
    }

    internal nint GetName(int entity)
    {
        if (Resolve(entity) is var (scene, index))
            return scene.Names[index];
        _log(LogLevel.Error, $"Entity {entity} is not in a loaded scene.");
        return EmptyString;
    }

    private static readonly nint EmptyString = Marshal.StringToCoTaskMemUTF8("");

    internal int Find(string name)
    {
        foreach (LoadedScene scene in _loaded)
        {
            for (int i = 0; i < scene.Scene.Entities.Count; i++)
                if (scene.Scene.Entities[i].Name == name)
                    return Handle(scene, i);
        }
        return -1;
    }

    internal nint GetScript(int entity, string type)
    {
        if (Resolve(entity) is not var (scene, index))
            return 0;
        RunningScript? script = scene.Scripts.FirstOrDefault(s => s.Entity == index && s.Type.Name == type);
        return script == null ? 0 : (nint)script.Memory;
    }

    internal bool GetButton(string name, int which) => which switch
    {
        0 => Input.GetButton(name),
        1 => Input.GetButtonDown(name),
        _ => Input.GetButtonUp(name),
    };

    internal bool GetKey(string name, int which)
    {
        if (!InputCodes.TryParse(name, out int code) || code == InputCodes.None)
        {
            _log(LogLevel.Error, $"There is no key named '{name}'.");
            return false;
        }
        return which switch
        {
            0 => Input.GetKey(code),
            1 => Input.GetKeyDown(code),
            _ => Input.GetKeyUp(code),
        };
    }

    internal void Log(int level, string message) =>
        _log(level == 0 ? LogLevel.Info : level == 1 ? LogLevel.Warning : LogLevel.Error, message);

    internal bool RequestScene(string? name, int index, int mode)
    {
        int target = name != null ? FindScene(name) : index;
        if (target < 0 || target >= _buildScenes.Count)
            return false;
        _request = (target, mode == 1 ? LoadSceneMode.Additive : LoadSceneMode.Single);
        return true;
    }

    internal void Quit() => QuitRequested = true;

    public void Dispose()
    {
        if (ScriptHost.Current == this)
            ScriptHost.Current = null;
        foreach (LoadedScene scene in _loaded)
            Unload(scene);
        _loaded.Clear();
        foreach (nint pointer in _strings)
            Marshal.FreeCoTaskMem(pointer);
        _strings.Clear();
    }
}

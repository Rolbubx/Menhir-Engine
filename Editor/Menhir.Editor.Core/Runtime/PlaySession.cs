using System.Numerics;
using Menhir.Editor.Build;
using Menhir.Editor.Input;
using Menhir.Editor.Scenes;
using Menhir.Editor.Scripting;
using Menhir.Editor.Serialization;
using Menhir.Editor.Logging;
using MenhirEngine;
using Entity = Menhir.Editor.Scenes.Entity;
using LoadSceneMode = MenhirEngine.LoadSceneMode;

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

    /// <summary>The scripts of each entity (by entity index), in component order.</summary>
    internal List<MenhirEngine.Script>?[] Scripts { get; set; } = [];
    internal List<FlyCameraBehaviour> FlyCameras { get; } = new();
    internal List<(SceneSwitcher Switcher, int Target)> Switchers { get; } = new();
}

/// <summary>
/// The game running in the editor (Play mode): copies of the build's scenes, the Input Manager,
/// the built-in behaviours and the project's scripts, hosting the scripting API. Each
/// <see cref="Update"/> is one frame, in the same order as the built game's player (mh_world.c):
/// input, Start (scripts not started yet), Update, built-in behaviours, LateUpdate, then the scene
/// change asked for during the frame. Exceptions thrown by scripts are logged, and the frame goes on.
/// </summary>
public sealed class PlaySession : IEngineHost, IDisposable
{
    private readonly IReadOnlyList<BuildScene> _buildScenes;
    private readonly ScriptAssembly? _scripts;
    private readonly Action<LogLevel, string> _log;
    private readonly List<LoadedScene> _loaded = new();
    private readonly List<MenhirEngine.Script> _notStarted = new();
    private int _nextSerial = 1;
    private (int Index, LoadSceneMode Mode)? _request;

    /// <param name="buildScenes">The scenes the game can load, by build index.</param>
    /// <param name="scripts">The compiled scripts (null: the project has none).</param>
    /// <param name="log">Debug.Log and script errors.</param>
    public PlaySession(IReadOnlyList<BuildScene> buildScenes, int firstScene, InputSettings input, ScriptAssembly? scripts,
        Action<LogLevel, string> log)
    {
        _buildScenes = buildScenes;
        _scripts = scripts;
        _log = log;
        Input = new InputState(input.Axes);
        EngineHosting.Current = this;
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

    /// <summary>Application.Quit was called.</summary>
    public bool QuitRequested { get; private set; }

    public void Update(InputFrame frame, float deltaTime)
    {
        EngineHosting.Current = this;
        Input.Update(frame, deltaTime);
        DeltaTime = deltaTime;
        Time += deltaTime;
        FrameCount++;

        foreach (MenhirEngine.Script script in _notStarted.ToList())
        {
            if (!script.enabled)
                continue;
            _notStarted.Remove(script);
            Run(script, "Start", s => s.Start());
        }
        foreach (MenhirEngine.Script script in AllScripts())
        {
            if (script.enabled && !_notStarted.Contains(script))
                Run(script, "Update", s => s.Update());
        }

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
                    _request = (target, switcher.Mode == Menhir.Editor.Scenes.LoadSceneMode.Additive ? LoadSceneMode.Additive : LoadSceneMode.Single);
            }
        }
        CursorCaptured = looking;

        foreach (MenhirEngine.Script script in AllScripts())
        {
            if (script.enabled && !_notStarted.Contains(script))
                Run(script, "LateUpdate", s => s.LateUpdate());
        }

        if (_request is { } request)
        {
            _request = null;
            Load(request.Index, request.Mode);
        }
    }

    private IEnumerable<MenhirEngine.Script> AllScripts()
    {
        foreach (LoadedScene scene in _loaded.ToList())
            foreach (List<MenhirEngine.Script>? scripts in scene.Scripts)
                if (scripts != null)
                    foreach (MenhirEngine.Script script in scripts.ToList())
                        yield return script;
    }

    private void Run(MenhirEngine.Script script, string method, Action<MenhirEngine.Script> call)
    {
        try
        {
            call(script);
        }
        catch (Exception e)
        {
            _log(LogLevel.Error, $"{script.GetType().Name}.{method} on '{script.name}': {e.GetType().Name}: {e.Message}");
        }
    }

    // ---- Scenes ---------------------------------------------------------------------------------

    private void Load(int index, LoadSceneMode mode)
    {
        if (index < 0 || index >= _buildScenes.Count)
            return;
        if (mode == LoadSceneMode.Single)
        {
            _loaded.Clear();
            _notStarted.Clear();
        }
        else if (_loaded.Any(s => s.Index == index))
            return;

        // A fresh copy: the editor's scene stays as it is.
        BuildScene source = _buildScenes[index];
        Scene copy = SceneSerializer.Deserialize(SceneSerializer.Serialize(source.Scene), []);
        var loaded = new LoadedScene(_nextSerial++, index, source.Name, copy);
        _loaded.Add(loaded);

        IReadOnlyList<Entity> entities = copy.Entities;
        loaded.Scripts = new List<MenhirEngine.Script>?[entities.Count];
        var created = new List<MenhirEngine.Script>();
        for (int i = 0; i < entities.Count; i++)
        {
            Entity entity = entities[i];
            foreach (Component component in entity.Components)
            {
                switch (component)
                {
                    case ScriptComponent sc when CreateScript(sc, entity) is { } script:
                        EngineHosting.Attach(script, Handle(loaded, i));
                        (loaded.Scripts[i] ??= new List<MenhirEngine.Script>()).Add(script);
                        created.Add(script);
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
        foreach (MenhirEngine.Script script in created)
            Run(script, "Awake", s => s.Awake());
        _notStarted.AddRange(created);
    }

    private MenhirEngine.Script? CreateScript(ScriptComponent component, Entity entity)
    {
        Type? type = _scripts?.Find(component.ClassName);
        if (type == null)
        {
            _log(LogLevel.Warning, $"{entity.Name}: the script {component.ClassName} does not exist (or does not compile).");
            return null;
        }
        MenhirEngine.Script script;
        try
        {
            script = (MenhirEngine.Script)Activator.CreateInstance(type)!;
        }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException is { } inner)
        {
            // Like the game's mh_scripts.c: the script is left out.
            _log(LogLevel.Error, $"{type.Name}..ctor on '{entity.Name}': {inner.GetType().Name}: {inner.Message}");
            return null;
        }
        foreach (string field in ScriptFields.Apply(script, component.Fields))
            _log(LogLevel.Warning, $"{entity.Name}: the saved value of {component.ClassName}.{field} does not fit the field; it keeps its default.");
        return script;
    }

    private int FindScene(string name) =>
        _buildScenes.Select((s, i) => (s, i)).FirstOrDefault(x => x.s.Name == name.Trim(), (null!, -1)).Item2;

    // ---- Entity handles: (load serial << 16) | entity index --------------------------------------

    private static int Handle(LoadedScene scene, int index) => (scene.Serial & 0x7FFF) << 16 | index;

    private (LoadedScene Scene, Entity Entity, int Index)? Resolve(int handle)
    {
        if (handle < 0)
            return null;
        int serial = handle >> 16, index = handle & 0xFFFF;
        LoadedScene? scene = _loaded.FirstOrDefault(s => (s.Serial & 0x7FFF) == serial);
        if (scene == null || index >= scene.Scene.Entities.Count)
            return null;
        return (scene, scene.Scene.Entities[index], index);
    }

    private Entity EntityOf(int handle) =>
        Resolve(handle)?.Entity ?? throw new InvalidOperationException("This entity is not in a loaded scene any more.");

    // ---- IEngineHost --------------------------------------------------------------------------

    float IEngineHost.GetFloat(int entity, int property)
    {
        Menhir.Editor.Scenes.Transform t = EntityOf(entity).Transform;
        return property switch
        {
            0 => t.Position.X, 1 => t.Position.Y, 2 => t.Position.Z,
            3 => t.Rotation.X, 4 => t.Rotation.Y, 5 => t.Rotation.Z, 6 => t.Rotation.W,
            7 => t.Scale.X, 8 => t.Scale.Y, _ => t.Scale.Z,
        };
    }

    void IEngineHost.SetFloat(int entity, int property, float value)
    {
        Menhir.Editor.Scenes.Transform t = EntityOf(entity).Transform;
        if (property < 3)
        {
            System.Numerics.Vector3 p = t.Position;
            if (property == 0) p.X = value; else if (property == 1) p.Y = value; else p.Z = value;
            t.Position = p;
        }
        else if (property < 7)
        {
            System.Numerics.Quaternion q = t.Rotation;
            if (property == 3) q.X = value; else if (property == 4) q.Y = value; else if (property == 5) q.Z = value; else q.W = value;
            t.Rotation = q;
        }
        else
        {
            System.Numerics.Vector3 s = t.Scale;
            if (property == 7) s.X = value; else if (property == 8) s.Y = value; else s.Z = value;
            t.Scale = s;
        }
    }

    int IEngineHost.GetParent(int entity)
    {
        if (Resolve(entity) is not var (scene, e, _) || e.ParentId == 0)
            return -1;
        int index = scene.Scene.Entities.ToList().FindIndex(x => x.LocalId == e.ParentId);
        return index < 0 ? -1 : Handle(scene, index);
    }

    string IEngineHost.GetName(int entity) => EntityOf(entity).Name;

    int IEngineHost.FindEntity(string name)
    {
        foreach (LoadedScene scene in _loaded)
        {
            for (int i = 0; i < scene.Scene.Entities.Count; i++)
                if (scene.Scene.Entities[i].Name == name)
                    return Handle(scene, i);
        }
        return -1;
    }

    int IEngineHost.GetScriptCount(int entity) =>
        Resolve(entity) is var (scene, _, index) ? scene.Scripts[index]?.Count ?? 0 : 0;

    object IEngineHost.GetScript(int entity, int index) =>
        Resolve(entity) is var (scene, _, i) ? scene.Scripts[i]![index] : null!;

    float IEngineHost.GetAxis(string name, bool raw) => raw ? Input.GetAxisRaw(name) : Input.GetAxis(name);

    bool IEngineHost.GetButton(string name, int which) => which switch
    {
        0 => Input.GetButton(name),
        1 => Input.GetButtonDown(name),
        _ => Input.GetButtonUp(name),
    };

    bool IEngineHost.GetKey(string name, int which)
    {
        if (!InputCodes.TryParse(name, out int code) || code == InputCodes.None)
            throw new ArgumentException($"Input Key named: {name} is unknown");
        return which switch
        {
            0 => Input.GetKey(code),
            1 => Input.GetKeyDown(code),
            _ => Input.GetKeyUp(code),
        };
    }

    float IEngineHost.GetMouse(int axis) => axis == 0 ? Input.MouseX : Input.MouseY;

    float IEngineHost.GetTime(int which) => which switch
    {
        0 => DeltaTime,
        1 => Time,
        _ => FrameCount,
    };

    void IEngineHost.Log(int level, string message) =>
        _log(level == 0 ? LogLevel.Info : level == 1 ? LogLevel.Warning : LogLevel.Error, message);

    bool IEngineHost.LoadScene(string name, int index, int mode)
    {
        int target = name != null ? FindScene(name) : index;
        if (target < 0 || target >= _buildScenes.Count)
            return false;
        _request = (target, (LoadSceneMode)mode);
        return true;
    }

    void IEngineHost.Quit() => QuitRequested = true;

    public void Dispose()
    {
        if (EngineHosting.Current == this)
            EngineHosting.Current = null;
        _loaded.Clear();
    }
}

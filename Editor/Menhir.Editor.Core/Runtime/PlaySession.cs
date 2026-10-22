using System.Numerics;
using System.Runtime.InteropServices;
using Menhir.Editor.Build;
using Menhir.Editor.Input;
using Menhir.Editor.Logging;
using Menhir.Editor.Scenes;
using Menhir.Editor.Scripting;
using Menhir.Editor.Serialization;

namespace Menhir.Editor.Runtime;

/// <summary>An entity of a loaded scene. Its index in <see cref="LoadedScene.Slots"/> never changes (handles carry it).</summary>
internal sealed class EntitySlot(Entity entity, nint name)
{
    public Entity Entity { get; } = entity;
    public nint Name { get; } = name; // UTF-8, for mh_get_name
    public bool Alive { get; set; } = true;
    public bool ActiveSelf { get; set; } = true;
}

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
    /// <summary>What is drawn: the entities that exist and are active.</summary>
    public Scene Scene { get; }

    /// <summary>Every entity the scene had, with the created and destroyed ones.</summary>
    internal List<EntitySlot> Slots { get; } = new();
    internal Dictionary<long, int> SlotsById { get; } = new();
    internal long NextLocalId { get; set; } = 1;
    internal List<RunningScript> Scripts { get; } = new();
    internal List<(FlyCameraBehaviour Behaviour, int Slot)> FlyCameras { get; } = new();
    internal List<(SceneSwitcher Switcher, int Target, int Slot)> Switchers { get; } = new();
}

/// <summary>A script of a loaded scene: its type and its struct (native memory).</summary>
internal sealed unsafe class RunningScript(int entity, ScriptType type, void* memory)
{
    public int Entity { get; } = entity;
    public ScriptType Type { get; } = type;
    public void* Memory { get; } = memory;
    public bool Started { get; set; }
    public bool Destroyed { get; set; }
}

/// <summary>
/// The game running in the editor (Play mode): copies of the build's scenes, the Input Manager, the
/// built-in behaviours and the project's C scripts (a <see cref="ScriptLibrary"/>), which reach the
/// engine through <see cref="ScriptHost"/>. Each <see cref="Update"/> is one frame, in the same order as
/// the built game's player (mh_world.c): input, Start (scripts not started yet), the fixed steps
/// (FixedUpdate, then the runtime's physics), Update, the runtime (timers, coroutines, tweens, audio),
/// built-in behaviours, LateUpdate, the entities destroyed during the frame, then the scene change asked
/// for. Scripts of inactive entities do not run. A script that crashes takes the editor down with it:
/// they run in its process.
/// </summary>
public sealed unsafe class PlaySession : IDisposable
{
    /// <summary>Seconds per fixed step (Unity's default).</summary>
    public const float FixedDeltaTime = 0.02f;
    /// <summary>At most this much time is caught up with fixed steps in one frame.</summary>
    public const float MaxFixedCatchUp = 0.25f;

    private static delegate* unmanaged<void*, int, void> Callback(nint pointer) => (delegate* unmanaged<void*, int, void>)pointer;

    private readonly IReadOnlyList<BuildScene> _buildScenes;
    private readonly ScriptLibrary? _scripts;
    private readonly Action<LogLevel, string> _log;
    private readonly string? _assetsDirectory;
    private readonly List<LoadedScene> _loaded = new();
    private readonly List<nint> _strings = new(); // native strings given to scripts, freed at the end
    private readonly Dictionary<string, nint> _assetPaths = new();
    private readonly List<int> _destroyQueue = new();
    private int _nextSerial = 1;
    private float _fixedTime;
    private bool _closed;
    private (int Index, LoadSceneMode Mode)? _request;

    /// <param name="buildScenes">The scenes the game can load, by build index.</param>
    /// <param name="scripts">The compiled scripts (null: none). The session uses it until disposed.</param>
    /// <param name="log">mh_log and the warnings about scripts.</param>
    /// <param name="assetsDirectory">The project's Assets folder (mh_asset_path).</param>
    public PlaySession(IReadOnlyList<BuildScene> buildScenes, int firstScene, InputSettings input, ScriptLibrary? scripts,
        Action<LogLevel, string> log, string? assetsDirectory = null)
    {
        _buildScenes = buildScenes;
        _scripts = scripts;
        _log = log;
        _assetsDirectory = assetsDirectory;
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

        RunScripts(start: true, c => c.Start);

        _fixedTime = MathF.Min(_fixedTime + deltaTime, MaxFixedCatchUp);
        while (_fixedTime >= FixedDeltaTime)
        {
            _fixedTime -= FixedDeltaTime;
            RunScripts(start: false, c => c.FixedUpdate);
            _scripts?.FixedStep();
        }

        RunScripts(start: false, c => c.Update);
        _scripts?.RuntimeUpdate();

        bool looking = false;
        foreach (LoadedScene scene in _loaded.ToList())
        {
            foreach ((FlyCameraBehaviour fly, int slot) in scene.FlyCameras)
            {
                if (!IsVisible(scene, slot))
                    continue;
                fly.Update(Input, deltaTime);
                looking |= fly.IsLooking;
            }
            foreach ((SceneSwitcher switcher, int target, int slot) in scene.Switchers)
            {
                if (IsVisible(scene, slot) && Input.GetButtonDown(switcher.Button))
                    _request = (target, switcher.Mode);
            }
        }
        CursorCaptured = looking;

        RunScripts(start: false, c => c.LateUpdate);
        FlushDestroyed();

        if (_request is { } request)
        {
            _request = null;
            Load(request.Index, request.Mode);
        }
    }

    /// <summary>Start runs once, before a script's first Update. Scripts created during the pass wait for the next one.</summary>
    private void RunScripts(bool start, Func<ScriptCallbacks, nint> callback)
    {
        foreach (LoadedScene scene in _loaded)
        {
            for (int i = 0, count = scene.Scripts.Count; i < count; i++)
            {
                RunningScript script = scene.Scripts[i];
                if (script.Destroyed || start == script.Started || !IsVisible(scene, script.Entity))
                    continue;
                script.Started = true;
                if (callback(script.Type.Callbacks) is not 0 and var function)
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
        var loaded = new LoadedScene(_nextSerial++, index, source.Name, copy) { NextLocalId = copy.NextLocalId };
        _loaded.Add(loaded);

        IReadOnlyList<Entity> entities = copy.Entities;
        for (int i = 0; i < entities.Count; i++)
            AddSlot(loaded, entities[i]);
        var created = new List<RunningScript>();
        for (int i = 0; i < entities.Count; i++)
        {
            Entity entity = entities[i];
            foreach (Component component in entity.Components)
            {
                switch (component)
                {
                    case ScriptComponent sc when CreateScript(sc, entity, i) is { } script:
                        created.Add(script);
                        break;
                    case FlyCamera fly when entity.Components.OfType<FlyCamera>().First() == fly:
                        loaded.FlyCameras.Add((new FlyCameraBehaviour(fly, entity.Transform), i));
                        break;
                    case SceneSwitcher switcher when entity.Components.OfType<SceneSwitcher>().First() == switcher:
                        int target = FindScene(switcher.Scene);
                        if (target >= 0)
                            loaded.Switchers.Add((switcher, target, i));
                        else
                            _log(LogLevel.Warning, $"The Scene Switcher of {entity.Name} loads '{switcher.Scene}', which is not in the build.");
                        break;
                }
            }
        }
        loaded.Scripts.AddRange(created);
        Awake(loaded, created);
    }

    private void Awake(LoadedScene scene, List<RunningScript> scripts)
    {
        foreach (RunningScript script in scripts)
        {
            if (!script.Destroyed && script.Type.Callbacks.Awake != 0)
                Callback(script.Type.Callbacks.Awake)(script.Memory, Handle(scene, script.Entity));
        }
    }

    private static int AddSlot(LoadedScene scene, Entity entity)
    {
        scene.Slots.Add(new EntitySlot(entity, Marshal.StringToCoTaskMemUTF8(entity.Name)));
        scene.SlotsById[entity.LocalId] = scene.Slots.Count - 1;
        scene.NextLocalId = Math.Max(scene.NextLocalId, entity.LocalId + 1);
        return scene.Slots.Count - 1;
    }

    private RunningScript? CreateScript(ScriptComponent component, Entity entity, int slot)
    {
        if (_scripts?.Find(component.ScriptName) is not { } type)
        {
            _log(LogLevel.Warning, $"{entity.Name}: the script {component.ScriptName} does not exist.");
            return null;
        }
        void* memory = Allocate(type);
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
        return new RunningScript(slot, type, memory);
    }

    private static void* Allocate(ScriptType type)
    {
        void* memory = NativeMemory.AlignedAlloc((nuint)Math.Max(type.Size, 1), 16);
        NativeMemory.Clear(memory, (nuint)Math.Max(type.Size, 1));
        return memory;
    }

    private nint NativeString(string text)
    {
        nint pointer = Marshal.StringToCoTaskMemUTF8(text);
        _strings.Add(pointer);
        return pointer;
    }

    /// <summary>OnDestroy for every script (they may still use the scene), then the memory goes.</summary>
    private void Unload(LoadedScene scene)
    {
        foreach (RunningScript script in scene.Scripts.ToList())
        {
            if (script.Destroyed)
                continue;
            if (script.Type.Callbacks.OnDestroy != 0)
                Callback(script.Type.Callbacks.OnDestroy)(script.Memory, Handle(scene, script.Entity));
        }
        foreach (RunningScript script in scene.Scripts)
        {
            if (!script.Destroyed)
                NativeMemory.AlignedFree(script.Memory);
            script.Destroyed = true;
        }
        scene.Scripts.Clear();
        foreach (EntitySlot slot in scene.Slots)
        {
            Marshal.FreeCoTaskMem(slot.Name);
            slot.Alive = false;
        }
        scene.Slots.Clear();
        scene.SlotsById.Clear();
    }

    private int FindScene(string name) =>
        _buildScenes.Select((s, i) => (s, i)).FirstOrDefault(x => x.s.Name == name.Trim(), (null!, -1)).Item2;

    // ---- Entity handles: (load serial << 24) | slot ------------------------------------------------

    private static int Handle(LoadedScene scene, int slot) => (scene.Serial & 0x7F) << 24 | slot;

    private (LoadedScene Scene, int Slot)? Resolve(int handle)
    {
        if (handle < 0)
            return null;
        int serial = handle >> 24, slot = handle & 0xFFFFFF;
        LoadedScene? scene = _loaded.FirstOrDefault(s => (s.Serial & 0x7F) == serial);
        if (scene == null || slot >= scene.Slots.Count || !scene.Slots[slot].Alive)
            return null;
        return (scene, slot);
    }

    private (LoadedScene Scene, int Slot)? ResolveOrLog(int handle)
    {
        if (Resolve(handle) is { } found)
            return found;
        _log(LogLevel.Error, $"Entity {handle} does not exist (destroyed, or its scene was unloaded).");
        return null;
    }

    private static int ParentSlot(LoadedScene scene, int slot) =>
        scene.Slots[slot].Entity.ParentId is not 0 and var id && scene.SlotsById.TryGetValue(id, out int parent) &&
        scene.Slots[parent].Alive ? parent : -1;

    /// <summary>Exists, and it and its parents are active.</summary>
    private static bool IsVisible(LoadedScene scene, int slot)
    {
        for (int depth = 0; slot >= 0 && depth <= scene.Slots.Count; depth++)
        {
            EntitySlot s = scene.Slots[slot];
            if (!s.Alive || !s.ActiveSelf)
                return false;
            slot = ParentSlot(scene, slot);
        }
        return true;
    }

    private static bool IsInSubtree(LoadedScene scene, int slot, int root)
    {
        for (int depth = 0; slot >= 0 && depth <= scene.Slots.Count; depth++)
        {
            if (slot == root)
                return true;
            slot = ParentSlot(scene, slot);
        }
        return false;
    }

    /// <summary>The scene shows the entities that exist and are active, in their order.</summary>
    private static void SyncScene(LoadedScene scene) =>
        scene.Scene.ReplaceEntities(scene.Slots.Where((_, i) => IsVisible(scene, i)).Select(s => s.Entity));

    private Scenes.Transform? TransformOf(int handle) =>
        ResolveOrLog(handle) is var (scene, slot) ? scene.Slots[slot].Entity.Transform : null;

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

    internal int GetParent(int entity) =>
        Resolve(entity) is var (scene, slot) && ParentSlot(scene, slot) is >= 0 and var parent ? Handle(scene, parent) : -1;

    internal nint GetName(int entity) => ResolveOrLog(entity) is var (scene, slot) ? scene.Slots[slot].Name : EmptyString;

    private static readonly nint EmptyString = Marshal.StringToCoTaskMemUTF8("");

    /// <summary>The first entity with that name (inactive ones too), in load order.</summary>
    internal int Find(string name)
    {
        foreach (LoadedScene scene in _loaded)
        {
            for (int i = 0; i < scene.Slots.Count; i++)
                if (scene.Slots[i].Alive && scene.Slots[i].Entity.Name == name)
                    return Handle(scene, i);
        }
        return -1;
    }

    internal nint GetScript(int entity, string type)
    {
        if (Resolve(entity) is not var (scene, slot))
            return 0;
        RunningScript? script = scene.Scripts.FirstOrDefault(s => !s.Destroyed && s.Entity == slot && s.Type.Name == type);
        return script == null ? 0 : (nint)script.Memory;
    }

    internal (nint Memory, nint Type) ScriptAt(int entity, int index)
    {
        if (Resolve(entity) is not var (scene, slot) || index < 0)
            return (0, 0);
        foreach (RunningScript script in scene.Scripts)
        {
            if (script.Destroyed || script.Entity != slot)
                continue;
            if (index-- == 0)
                return ((nint)script.Memory, script.Type.Native);
        }
        return (0, 0);
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

    internal bool IsAlive(int entity) => Resolve(entity) != null;

    /// <summary>A copy of the entity and its children, with their components and their scripts' fields as they are now.</summary>
    internal int Instantiate(int entity, int parent)
    {
        if (ResolveOrLog(entity) is not var (source, root))
            return -1;
        LoadedScene target = source;
        int parentSlot = -1;
        if (parent >= 0)
        {
            if (ResolveOrLog(parent) is not var (parentScene, p))
                return -1;
            target = parentScene;
            parentSlot = p;
        }

        // The subtree, copied through the scene format (components included).
        List<int> subtree = Enumerable.Range(0, source.Slots.Count)
            .Where(i => source.Slots[i].Alive && IsInSubtree(source, i, root)).ToList();
        var shallow = subtree.Select(i =>
        {
            Entity e = source.Slots[i].Entity;
            var copy = new Entity { LocalId = e.LocalId, ParentId = i == root ? 0 : e.ParentId, Name = e.Name };
            copy.Transform.Position = e.Transform.Position;
            copy.Transform.Rotation = e.Transform.Rotation;
            copy.Transform.Scale = e.Transform.Scale;
            copy.Components.AddRange(e.Components);
            return copy;
        }).ToList();
        Scene clones = SceneSerializer.Deserialize(SceneSerializer.Serialize(Scene.FromEntities(shallow, 1, out _)), []);

        var ids = new Dictionary<long, long>();
        foreach (Entity e in clones.Entities)
            ids[e.LocalId] = target.NextLocalId++;
        var created = new List<RunningScript>();
        int first = -1;
        for (int k = 0; k < subtree.Count; k++)
        {
            Entity clone = clones.Entities[k];
            var entityCopy = new Entity
            {
                LocalId = ids[clone.LocalId],
                ParentId = k == 0 ? (parentSlot >= 0 ? target.Slots[parentSlot].Entity.LocalId : 0) : ids.GetValueOrDefault(clone.ParentId),
                Name = clone.Name,
            };
            entityCopy.Transform.Position = clone.Transform.Position;
            entityCopy.Transform.Rotation = clone.Transform.Rotation;
            entityCopy.Transform.Scale = clone.Transform.Scale;
            entityCopy.Components.AddRange(clone.Components);
            int slot = AddSlot(target, entityCopy);
            target.Slots[slot].ActiveSelf = source.Slots[subtree[k]].ActiveSelf;
            if (k == 0)
                first = slot;

            // The scripts, with their fields' values now (their other variables start from zero).
            List<RunningScript> originals = source.Scripts.Where(s => !s.Destroyed && s.Entity == subtree[k]).ToList();
            foreach (RunningScript original in originals)
            {
                void* memory = Allocate(original.Type);
                foreach (ScriptField field in original.Type.Fields)
                    ScriptFields.Write(memory, field, ScriptFields.Read(original.Memory, field), NativeString);
                created.Add(new RunningScript(slot, original.Type, memory));
            }
        }
        target.Scripts.AddRange(created);
        SyncScene(target);
        Awake(target, created);
        return Handle(target, first);
    }

    internal void Destroy(int entity)
    {
        if (Resolve(entity) != null && !_destroyQueue.Contains(entity))
            _destroyQueue.Add(entity);
    }

    /// <summary>The entities destroyed during the frame go, with their children: OnDestroy first.</summary>
    private void FlushDestroyed()
    {
        while (_destroyQueue.Count > 0)
        {
            int handle = _destroyQueue[0];
            _destroyQueue.RemoveAt(0);
            if (Resolve(handle) is not var (scene, root))
                continue;
            var subtree = Enumerable.Range(0, scene.Slots.Count).Where(i => scene.Slots[i].Alive && IsInSubtree(scene, i, root)).ToHashSet();
            List<RunningScript> scripts = scene.Scripts.Where(s => !s.Destroyed && subtree.Contains(s.Entity)).ToList();
            foreach (RunningScript script in scripts)
            {
                if (script.Type.Callbacks.OnDestroy != 0)
                    Callback(script.Type.Callbacks.OnDestroy)(script.Memory, Handle(scene, script.Entity));
            }
            foreach (RunningScript script in scripts)
            {
                script.Destroyed = true;
                NativeMemory.AlignedFree(script.Memory);
            }
            scene.Scripts.RemoveAll(s => s.Destroyed);
            foreach (int slot in subtree)
                scene.Slots[slot].Alive = false;
            SyncScene(scene);
        }
    }

    internal void SetActive(int entity, bool active)
    {
        if (ResolveOrLog(entity) is not var (scene, slot))
            return;
        scene.Slots[slot].ActiveSelf = active;
        SyncScene(scene);
    }

    internal bool GetActive(int entity, bool inHierarchy) =>
        Resolve(entity) is var (scene, slot) && (inHierarchy ? IsVisible(scene, slot) : scene.Slots[slot].ActiveSelf);

    private IEnumerable<int> Children(LoadedScene scene, int slot) =>
        Enumerable.Range(0, scene.Slots.Count).Where(i => scene.Slots[i].Alive && ParentSlot(scene, i) == slot);

    internal int ChildCount(int entity) => Resolve(entity) is var (scene, slot) ? Children(scene, slot).Count() : 0;

    internal int ChildAt(int entity, int index) =>
        Resolve(entity) is var (scene, slot) && Children(scene, slot).Skip(index).Take(1).ToList() is [var child] && index >= 0
            ? Handle(scene, child) : -1;

    internal void SetParent(int entity, int parent)
    {
        if (ResolveOrLog(entity) is not var (scene, slot))
            return;
        long parentId = 0;
        if (parent >= 0)
        {
            if (ResolveOrLog(parent) is not var (parentScene, p))
                return;
            if (parentScene != scene || IsInSubtree(scene, p, slot))
            {
                _log(LogLevel.Error, $"{scene.Slots[slot].Entity.Name} cannot go under {parentScene.Slots[p].Entity.Name} " +
                                     "(another scene, or one of its own children).");
                return;
            }
            parentId = scene.Slots[p].Entity.LocalId;
        }
        scene.Slots[slot].Entity.ParentId = parentId;
        SyncScene(scene);
    }

    internal nint AssetPath(string relative)
    {
        if (!_assetPaths.TryGetValue(relative, out nint path))
        {
            path = NativeString(_assetsDirectory != null ? Path.Combine(_assetsDirectory, relative) : relative);
            _assetPaths[relative] = path;
        }
        return path;
    }

    /// <summary>The entity of the first camera of the loaded scenes (what the Game panel renders with).</summary>
    internal int MainCamera()
    {
        foreach (LoadedScene scene in _loaded)
        {
            for (int i = 0; i < scene.Slots.Count; i++)
                if (IsVisible(scene, i) && scene.Slots[i].Entity.GetComponent<Camera>() != null)
                    return Handle(scene, i);
        }
        return -1;
    }

    /// <summary>The game is closing: OnGameClose, then every scene unloads (OnDestroy), then the runtime stops.</summary>
    public void Dispose()
    {
        ScriptHost.Current = this;
        if (!_closed)
        {
            _closed = true;
            foreach (LoadedScene scene in _loaded)
            {
                for (int i = 0, count = scene.Scripts.Count; i < count; i++)
                {
                    RunningScript script = scene.Scripts[i];
                    if (!script.Destroyed && script.Type.Callbacks.OnGameClose != 0)
                        Callback(script.Type.Callbacks.OnGameClose)(script.Memory, Handle(scene, script.Entity));
                }
            }
        }
        foreach (LoadedScene scene in _loaded)
            Unload(scene);
        _loaded.Clear();
        _scripts?.Shutdown();
        if (ScriptHost.Current == this)
            ScriptHost.Current = null;
        foreach (nint pointer in _strings)
            Marshal.FreeCoTaskMem(pointer);
        _strings.Clear();
        _assetPaths.Clear();
    }
}

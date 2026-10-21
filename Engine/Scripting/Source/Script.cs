namespace MenhirEngine
{
    /// <summary>
    /// Base class of the game's scripts, like Unity's MonoBehaviour. Add a script to an entity with
    /// Add Component → Scripts; its public fields show in the inspector and are saved with the scene.
    /// Every frame: Start (once, before the first Update), Update, then LateUpdate.
    /// </summary>
    public abstract class Script
    {
        internal int _entity = -1;
        private Entity _entityObject;
        private Transform _transform;

        /// <summary>Unity's enabled: a disabled script gets no Update and LateUpdate.</summary>
        public bool enabled = true;

        public Entity entity => _entityObject ??= new Entity(_entity);
        /// <summary>The same as <see cref="entity"/> (Unity's name).</summary>
        public Entity gameObject => entity;
        public Transform transform => _transform ??= new Transform(_entity);
        public string name => Host.GetName(_entity);

        /// <summary>When the scene is loaded, before any Start.</summary>
        public virtual void Awake() { }

        /// <summary>Before the script's first Update.</summary>
        public virtual void Start() { }

        public virtual void Update() { }

        /// <summary>After every Update of the frame (cameras following something).</summary>
        public virtual void LateUpdate() { }

        public T GetComponent<T>() where T : Script => entity.GetComponent<T>();

        public static void print(object message) => Debug.Log(message);
    }

    /// <summary>An entity of a loaded scene (valid while its scene is loaded).</summary>
    public sealed class Entity
    {
        private readonly int _handle;
        private Transform _transform;

        internal Entity(int handle) { _handle = handle; }

        public string name => Host.GetName(_handle);
        public Transform transform => _transform ??= new Transform(_handle);

        /// <summary>The first script of type T on the entity, or null.</summary>
        public T GetComponent<T>() where T : Script
        {
            int count = Host.GetScriptCount(_handle);
            for (int i = 0; i < count; i++)
            {
                if (Host.GetScript(_handle, i) is T script)
                    return script;
            }
            return null;
        }

        /// <summary>The first entity with this name in the loaded scenes, or null.</summary>
        public static Entity Find(string name)
        {
            int handle = Host.FindEntity(name);
            return handle >= 0 ? new Entity(handle) : null;
        }

        public override bool Equals(object obj) => obj is Entity other && other._handle == _handle;

        public override int GetHashCode() => _handle;

        public override string ToString() => name;
    }

    public enum LoadSceneMode
    {
        /// <summary>Unloads the loaded scenes first.</summary>
        Single,
        /// <summary>Adds the scene to the loaded ones.</summary>
        Additive,
    }

    /// <summary>Loads scenes of the build (Project Settings → Build), by name or index, at the end of the frame.</summary>
    public static class SceneManager
    {
        public static void LoadScene(string name, LoadSceneMode mode = LoadSceneMode.Single)
        {
            if (!Host.LoadScene(name, -1, (int)mode))
                Debug.LogError("Scene '" + name + "' is not in the build.");
        }

        public static void LoadScene(int index, LoadSceneMode mode = LoadSceneMode.Single)
        {
            if (!Host.LoadScene(null, index, (int)mode))
                Debug.LogError("There is no scene " + index + " in the build.");
        }
    }

    public static class Debug
    {
        public static void Log(object message) => Host.Log(0, message == null ? "Null" : message.ToString());
        public static void LogWarning(object message) => Host.Log(1, message == null ? "Null" : message.ToString());
        public static void LogError(object message) => Host.Log(2, message == null ? "Null" : message.ToString());
    }

    public static class Time
    {
        /// <summary>Seconds since the previous frame.</summary>
        public static float deltaTime => Host.GetTime(0);
        /// <summary>Seconds since Play started (or the game).</summary>
        public static float time => Host.GetTime(1);
        public static int frameCount => (int)Host.GetTime(2);
    }

    public static class Application
    {
        /// <summary>Ends the game (stops Play mode in the editor).</summary>
        public static void Quit() => Host.Quit();
    }

    /// <summary>
    /// The Input Manager (Project Settings → Input), like Unity's Input class: axes and buttons by
    /// name, keys by name ("space", "a", "left shift", "mouse 0"... see Docs/Input.md).
    /// </summary>
    public static class Input
    {
        public static float GetAxis(string axisName) => Host.GetAxis(axisName, false);
        public static float GetAxisRaw(string axisName) => Host.GetAxis(axisName, true);
        public static bool GetButton(string buttonName) => Host.GetButton(buttonName, 0);
        public static bool GetButtonDown(string buttonName) => Host.GetButton(buttonName, 1);
        public static bool GetButtonUp(string buttonName) => Host.GetButton(buttonName, 2);
        public static bool GetKey(string name) => Host.GetKey(name, 0);
        public static bool GetKeyDown(string name) => Host.GetKey(name, 1);
        public static bool GetKeyUp(string name) => Host.GetKey(name, 2);
        public static bool GetMouseButton(int button) => Host.GetKey("mouse " + button, 0);
        public static bool GetMouseButtonDown(int button) => Host.GetKey("mouse " + button, 1);
        public static bool GetMouseButtonUp(int button) => Host.GetKey("mouse " + button, 2);
        /// <summary>The cursor in pixels from the window's bottom-left corner.</summary>
        public static Vector3 mousePosition => new Vector3(Host.GetMouse(0), Host.GetMouse(1), 0f);
    }

    /// <summary>Random numbers for gameplay, like Unity's Random (seed it with InitState for repeatable runs).</summary>
    public static class Random
    {
        private static System.Random _random = new System.Random();

        public static void InitState(int seed) => _random = new System.Random(seed);

        /// <summary>0 to 1, both included.</summary>
        public static float value => (float)_random.NextDouble();

        /// <summary>min to max, both included.</summary>
        public static float Range(float min, float max) => min + (float)_random.NextDouble() * (max - min);

        /// <summary>min included, max excluded.</summary>
        public static int Range(int min, int max) => min < max ? _random.Next(min, max) : min;

        public static Vector3 insideUnitSphere
        {
            get
            {
                while (true)
                {
                    var v = new Vector3(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f));
                    if (v.sqrMagnitude <= 1f)
                        return v;
                }
            }
        }
    }
}

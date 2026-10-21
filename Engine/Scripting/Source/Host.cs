#if MENHIR_NATIVE
using System.Runtime.CompilerServices;
#endif

namespace MenhirEngine
{
    /// <summary>
    /// Everything the scripting API asks the engine. In the built game these are internal calls
    /// into the C player (mh_script_host.c); in the editor's Play mode they go to <see cref="IEngineHost"/>.
    /// Entities are handles: valid while their scene stays loaded.
    /// </summary>
    internal static class Host
    {
        // Transform properties for GetFloat / SetFloat.
        public const int PositionX = 0, RotationX = 3, ScaleX = 7;

#if MENHIR_NATIVE
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float GetFloat(int entity, int property);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern void SetFloat(int entity, int property, float value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern int GetParent(int entity);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern string GetName(int entity);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern int FindEntity(string name);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern int GetScriptCount(int entity);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern object GetScript(int entity, int index);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float GetAxis(string name, bool raw);
        /// <summary>which: 0 held, 1 went down this frame, 2 went up this frame.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool GetButton(string name, int which);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool GetKey(string name, int which);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float GetMouse(int axis);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float GetTime(int which);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern void Log(int level, string message);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern bool LoadScene(string name, int index, int mode);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern void Quit();
#else
        public static IEngineHost Current;

        private static IEngineHost H => Current ?? throw new System.InvalidOperationException("The engine is not running (scripts run in Play mode or in the built game).");

        public static float GetFloat(int entity, int property) => H.GetFloat(entity, property);
        public static void SetFloat(int entity, int property, float value) => H.SetFloat(entity, property, value);
        public static int GetParent(int entity) => H.GetParent(entity);
        public static string GetName(int entity) => H.GetName(entity);
        public static int FindEntity(string name) => H.FindEntity(name);
        public static int GetScriptCount(int entity) => H.GetScriptCount(entity);
        public static object GetScript(int entity, int index) => H.GetScript(entity, index);
        public static float GetAxis(string name, bool raw) => H.GetAxis(name, raw);
        public static bool GetButton(string name, int which) => H.GetButton(name, which);
        public static bool GetKey(string name, int which) => H.GetKey(name, which);
        public static float GetMouse(int axis) => H.GetMouse(axis);
        public static float GetTime(int which) => H.GetTime(which);
        public static void Log(int level, string message) => H.Log(level, message);
        public static bool LoadScene(string name, int index, int mode) => H.LoadScene(name, index, mode);
        public static void Quit() => H.Quit();
#endif
    }

#if !MENHIR_NATIVE
    /// <summary>The engine side of the scripting API in the editor's Play mode (see Host).</summary>
    public interface IEngineHost
    {
        float GetFloat(int entity, int property);
        void SetFloat(int entity, int property, float value);
        int GetParent(int entity);
        string GetName(int entity);
        int FindEntity(string name);
        int GetScriptCount(int entity);
        object GetScript(int entity, int index);
        float GetAxis(string name, bool raw);
        bool GetButton(string name, int which);
        bool GetKey(string name, int which);
        float GetMouse(int axis);
        float GetTime(int which);
        void Log(int level, string message);
        bool LoadScene(string name, int index, int mode);
        void Quit();
    }

    /// <summary>Lets the editor plug its Play mode in, and give scripts their entity.</summary>
    public static class EngineHosting
    {
        public static IEngineHost Current
        {
            get => Host.Current;
            set => Host.Current = value;
        }

        public static void Attach(Script script, int entity) => script._entity = entity;

        public static int EntityOf(Script script) => script._entity;
    }
#endif
}

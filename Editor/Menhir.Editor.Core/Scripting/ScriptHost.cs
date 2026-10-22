using System.Runtime.InteropServices;
using Menhir.Editor.Runtime;

namespace Menhir.Editor.Scripting;

/// <summary>
/// The engine side of the scripting API in Play mode: the mh_api table of menhir_abi.h, whose functions
/// forward to the running <see cref="PlaySession"/>. An exception in them would end the editor (it
/// cannot cross into C), so each one logs it instead.
/// </summary>
public static unsafe class ScriptHost
{
    private const int FunctionCount = 25;

    public static uint ApiSize => (uint)(FunctionCount * sizeof(nint));

    /// <summary>The table to give the scripts' mh_bind; it lives as long as the editor.</summary>
    public static void* Api { get; } = CreateApi();

    /// <summary>The session the scripts are running in.</summary>
    internal static PlaySession? Current { get; set; }

    private static void* CreateApi()
    {
        var table = (nint*)NativeMemory.AllocZeroed(FunctionCount, (nuint)sizeof(nint));
        int i = 0;
        // In the order of mh_api's members.
        table[i++] = (nint)(delegate* unmanaged<int, int, float*, void>)&GetTransform;
        table[i++] = (nint)(delegate* unmanaged<int, int, float*, void>)&SetTransform;
        table[i++] = (nint)(delegate* unmanaged<int, int>)&GetParent;
        table[i++] = (nint)(delegate* unmanaged<int, byte*>)&GetName;
        table[i++] = (nint)(delegate* unmanaged<byte*, int>)&Find;
        table[i++] = (nint)(delegate* unmanaged<int, byte*, void*>)&GetScript;
        table[i++] = (nint)(delegate* unmanaged<byte*, int, float>)&GetAxis;
        table[i++] = (nint)(delegate* unmanaged<byte*, int, int>)&GetButton;
        table[i++] = (nint)(delegate* unmanaged<byte*, int, int>)&GetKey;
        table[i++] = (nint)(delegate* unmanaged<float*, void>)&GetMousePosition;
        table[i++] = (nint)(delegate* unmanaged<int, float>)&GetTime;
        table[i++] = (nint)(delegate* unmanaged<int, byte*, void>)&Log;
        table[i++] = (nint)(delegate* unmanaged<byte*, int, int, int>)&LoadScene;
        table[i++] = (nint)(delegate* unmanaged<void>)&Quit;
        table[i++] = (nint)(delegate* unmanaged<int, int>)&IsAlive;
        table[i++] = (nint)(delegate* unmanaged<int, int, int>)&Instantiate;
        table[i++] = (nint)(delegate* unmanaged<int, void>)&Destroy;
        table[i++] = (nint)(delegate* unmanaged<int, int, void>)&SetActive;
        table[i++] = (nint)(delegate* unmanaged<int, int, int>)&GetActive;
        table[i++] = (nint)(delegate* unmanaged<int, int>)&ChildCount;
        table[i++] = (nint)(delegate* unmanaged<int, int, int>)&ChildAt;
        table[i++] = (nint)(delegate* unmanaged<int, int, void>)&SetParent;
        table[i++] = (nint)(delegate* unmanaged<int, int, nint*, void*>)&ScriptAt;
        table[i++] = (nint)(delegate* unmanaged<byte*, byte*>)&AssetPath;
        table[i++] = (nint)(delegate* unmanaged<int>)&MainCamera;
        System.Diagnostics.Debug.Assert(i == FunctionCount);
        return table;
    }

    private static string Text(byte* utf8) => utf8 == null ? "" : Marshal.PtrToStringUTF8((nint)utf8) ?? "";

    private static T Guard<T>(Func<PlaySession, T> call, T fallback)
    {
        try
        {
            return Current is { } session ? call(session) : fallback;
        }
        catch (Exception e)
        {
            Logging.Log.Error("The scripting API failed.", e);
            return fallback;
        }
    }

    [UnmanagedCallersOnly]
    private static void GetTransform(int entity, int what, float* output) =>
        Guard(s => { s.GetTransform(entity, what, new Span<float>(output, what == 1 ? 4 : 3)); return 0; }, 0);

    [UnmanagedCallersOnly]
    private static void SetTransform(int entity, int what, float* values) =>
        Guard(s => { s.SetTransform(entity, what, new ReadOnlySpan<float>(values, what == 1 ? 4 : 3)); return 0; }, 0);

    [UnmanagedCallersOnly]
    private static int GetParent(int entity) => Guard(s => s.GetParent(entity), -1);

    [UnmanagedCallersOnly]
    private static byte* GetName(int entity) => (byte*)Guard(s => s.GetName(entity), 0);

    [UnmanagedCallersOnly]
    private static int Find(byte* name) => Guard(s => s.Find(Text(name)), -1);

    [UnmanagedCallersOnly]
    private static void* GetScript(int entity, byte* type) => (void*)Guard(s => s.GetScript(entity, Text(type)), 0);

    [UnmanagedCallersOnly]
    private static float GetAxis(byte* name, int raw) =>
        Guard(s => raw != 0 ? s.Input.GetAxisRaw(Text(name)) : s.Input.GetAxis(Text(name)), 0f);

    [UnmanagedCallersOnly]
    private static int GetButton(byte* name, int which) => Guard(s => s.GetButton(Text(name), which) ? 1 : 0, 0);

    [UnmanagedCallersOnly]
    private static int GetKey(byte* name, int which) => Guard(s => s.GetKey(Text(name), which) ? 1 : 0, 0);

    [UnmanagedCallersOnly]
    private static void GetMousePosition(float* output) =>
        Guard(s => { output[0] = s.Input.MouseX; output[1] = s.Input.MouseY; return 0; }, 0);

    [UnmanagedCallersOnly]
    private static float GetTime(int which) =>
        Guard(s => which switch { 0 => s.DeltaTime, 1 => s.Time, 2 => s.FrameCount, _ => PlaySession.FixedDeltaTime }, 0f);

    [UnmanagedCallersOnly]
    private static void Log(int level, byte* message) => Guard(s => { s.Log(level, Text(message)); return 0; }, 0);

    [UnmanagedCallersOnly]
    private static int LoadScene(byte* name, int index, int mode) =>
        Guard(s => s.RequestScene(name == null ? null : Text(name), index, mode) ? 1 : 0, 0);

    [UnmanagedCallersOnly]
    private static void Quit() => Guard(s => { s.Quit(); return 0; }, 0);

    [UnmanagedCallersOnly]
    private static int IsAlive(int entity) => Guard(s => s.IsAlive(entity) ? 1 : 0, 0);

    [UnmanagedCallersOnly]
    private static int Instantiate(int entity, int parent) => Guard(s => s.Instantiate(entity, parent), -1);

    [UnmanagedCallersOnly]
    private static void Destroy(int entity) => Guard(s => { s.Destroy(entity); return 0; }, 0);

    [UnmanagedCallersOnly]
    private static void SetActive(int entity, int active) => Guard(s => { s.SetActive(entity, active != 0); return 0; }, 0);

    [UnmanagedCallersOnly]
    private static int GetActive(int entity, int inHierarchy) => Guard(s => s.GetActive(entity, inHierarchy != 0) ? 1 : 0, 0);

    [UnmanagedCallersOnly]
    private static int ChildCount(int entity) => Guard(s => s.ChildCount(entity), 0);

    [UnmanagedCallersOnly]
    private static int ChildAt(int entity, int index) => Guard(s => s.ChildAt(entity, index), -1);

    [UnmanagedCallersOnly]
    private static void SetParent(int entity, int parent) => Guard(s => { s.SetParent(entity, parent); return 0; }, 0);

    [UnmanagedCallersOnly]
    private static void* ScriptAt(int entity, int index, nint* type) =>
        (void*)Guard(s =>
        {
            (nint memory, nint native) = s.ScriptAt(entity, index);
            if (type != null)
                *type = native;
            return memory;
        }, 0);

    [UnmanagedCallersOnly]
    private static byte* AssetPath(byte* relative) => (byte*)Guard(s => s.AssetPath(Text(relative)), 0);

    [UnmanagedCallersOnly]
    private static int MainCamera() => Guard(s => s.MainCamera(), -1);
}

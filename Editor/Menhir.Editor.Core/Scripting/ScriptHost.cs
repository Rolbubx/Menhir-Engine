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
    private const int FunctionCount = 14;

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
        Guard(s => which == 0 ? s.DeltaTime : which == 1 ? s.Time : s.FrameCount, 0f);

    [UnmanagedCallersOnly]
    private static void Log(int level, byte* message) => Guard(s => { s.Log(level, Text(message)); return 0; }, 0);

    [UnmanagedCallersOnly]
    private static int LoadScene(byte* name, int index, int mode) =>
        Guard(s => s.RequestScene(name == null ? null : Text(name), index, mode) ? 1 : 0, 0);

    [UnmanagedCallersOnly]
    private static void Quit() => Guard(s => { s.Quit(); return 0; }, 0);
}

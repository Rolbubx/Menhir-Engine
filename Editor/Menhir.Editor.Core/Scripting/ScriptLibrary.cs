using System.Runtime.InteropServices;

namespace Menhir.Editor.Scripting;

/// <summary>MH_FIELD_* of menhir_abi.h.</summary>
public enum ScriptFieldKind
{
    Int,
    Float,
    Bool,
    Vector2,
    Vector3,
    Color,
    String,
    Enum,
}

/// <summary>A field of a script, as its MH_SCRIPT declares it.</summary>
public sealed record ScriptField(string Name, ScriptFieldKind Kind, int Offset, double[] Default, string DefaultText,
    float? Min, float? Max, string? Tooltip, IReadOnlyList<string> Options);

/// <summary>Where a script type's callbacks are (mh_script_type), in the library it was read from, while it is loaded.</summary>
public sealed record ScriptCallbacks(nint Awake, nint Start, nint Update, nint LateUpdate, nint FixedUpdate, nint OnDestroy,
    nint OnGameClose)
{
    public static readonly ScriptCallbacks None = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// A script type of the project (or a built-in component of the runtime, e.g. Rigidbody). Native is its
/// mh_script_type, and Callbacks its functions, while the library it was read from is loaded.
/// </summary>
public sealed record ScriptType(string Name, int Size, IReadOnlyList<ScriptField> Fields, nint Native, ScriptCallbacks Callbacks);

/// <summary>
/// The project's scripts compiled into a shared library (see <see cref="ScriptCompiler"/>), loaded in the
/// editor. Each load is a copy of the file, so a new compilation can replace it, and so every Play
/// starts with the scripts' global variables as they were declared.
/// </summary>
public sealed unsafe class ScriptLibrary : IDisposable
{
    // Copies of menhir_abi.h's structs (64-bit layout, like the C compiler's).
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeField
    {
        public byte* Name;
        public int Kind;
        public uint Offset;
        public fixed double Number[4];
        public byte* Text;
        public int HasRange;
        public float Min, Max;
        public byte* Tooltip;
        public byte** Options;
        public int OptionCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeScriptType
    {
        public byte* Name;
        public uint Size;
        public NativeField* Fields;
        public int FieldCount;
        public nint Awake, Start, Update, LateUpdate, FixedUpdate, OnDestroy, OnGameClose;
        public nint CollisionEnter, CollisionExit, TriggerEnter, TriggerExit;
    }

    private readonly nint _handle;
    private readonly string _copy;
    private readonly delegate* unmanaged<void*, void> _bind;
    private readonly delegate* unmanaged<void> _update, _fixedStep, _shutdown;

    private ScriptLibrary(nint handle, string copy)
    {
        _handle = handle;
        _copy = copy;
        var sizesFunction = (delegate* unmanaged<uint*, void>)Export("mh_abi_sizes");
        uint* sizes = stackalloc uint[3];
        sizesFunction(sizes);
        if (sizes[0] != sizeof(NativeField) || sizes[1] != sizeof(NativeScriptType) || sizes[2] != ScriptHost.ApiSize)
            throw new InvalidDataException(
                $"The scripts' library does not match the editor (structure sizes {sizes[0]}, {sizes[1]}, {sizes[2]}).");
        _bind = (delegate* unmanaged<void*, void>)Export("mh_bind");
        _update = (delegate* unmanaged<void>)Export("mh_runtime_update");
        _fixedStep = (delegate* unmanaged<void>)Export("mh_runtime_fixed_step");
        _shutdown = (delegate* unmanaged<void>)Export("mh_runtime_shutdown");

        var count = (delegate* unmanaged<int>)Export("mh_script_type_count");
        var at = (delegate* unmanaged<int, NativeScriptType*>)Export("mh_script_type_at");
        var types = new List<ScriptType>();
        for (int i = 0, n = count(); i < n; i++)
            types.Add(Read(at(i)));
        Types = types;
    }

    public IReadOnlyList<ScriptType> Types { get; }

    /// <summary>Loads a copy of <paramref name="path"/> (the copy is deleted when disposed).</summary>
    /// <exception cref="DllNotFoundException">The library cannot be loaded (e.g. Windows' Smart App Control blocks it).</exception>
    /// <exception cref="InvalidDataException">It was built for another version of the API.</exception>
    public static ScriptLibrary Load(string path)
    {
        string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "Loaded");
        Directory.CreateDirectory(directory);
        string copy = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(path)}-{Guid.NewGuid():N}{Path.GetExtension(path)}");
        File.Copy(path, copy);
        nint handle;
        try
        {
            handle = NativeLibrary.Load(copy);
        }
        catch
        {
            TryDelete(copy);
            throw;
        }
        try
        {
            return new ScriptLibrary(handle, copy);
        }
        catch
        {
            NativeLibrary.Free(handle);
            TryDelete(copy);
            throw;
        }
    }

    /// <summary>The types of a compiled library, read from a copy that is unloaded at once.</summary>
    public static IReadOnlyList<ScriptType> ReadTypes(string path)
    {
        using ScriptLibrary library = Load(path);
        // The callbacks are only valid while the copy is loaded.
        return library.Types.Select(t => t with { Native = 0, Callbacks = ScriptCallbacks.None }).ToList();
    }

    /// <summary>Gives the scripts the engine (menhir_script.c's mh_bind, which also seeds mh_random).</summary>
    public void Bind(void* api) => _bind(api);

    /// <summary>The runtime's timers, coroutines, tweens and audio, after the scripts' Update (mh_runtime_update).</summary>
    public void RuntimeUpdate() => _update();

    /// <summary>The physics, after the scripts' FixedUpdate (mh_runtime_fixed_step).</summary>
    public void FixedStep() => _fixedStep();

    /// <summary>The game ended: the runtime stops its sounds and forgets everything (mh_runtime_shutdown).</summary>
    public void Shutdown() => _shutdown();

    public ScriptType? Find(string name) => Types.FirstOrDefault(t => t.Name == name);

    private nint Export(string name) =>
        NativeLibrary.TryGetExport(_handle, name, out nint address)
            ? address
            : throw new InvalidDataException($"The scripts' library has no {name}: it was not built with menhir_script.c.");

    private static ScriptType Read(NativeScriptType* t)
    {
        var fields = new List<ScriptField>();
        for (int i = 0; i < t->FieldCount; i++)
        {
            NativeField* f = &t->Fields[i];
            var options = new List<string>();
            for (int o = 0; o < f->OptionCount; o++)
                options.Add(Text(f->Options[o]));
            fields.Add(new ScriptField(Text(f->Name), (ScriptFieldKind)f->Kind, (int)f->Offset,
                [f->Number[0], f->Number[1], f->Number[2], f->Number[3]], Text(f->Text),
                f->HasRange != 0 ? f->Min : null, f->HasRange != 0 ? f->Max : null,
                f->Tooltip != null ? Text(f->Tooltip) : null, options));
        }
        return new ScriptType(Text(t->Name), (int)t->Size, fields, (nint)t,
            new ScriptCallbacks(t->Awake, t->Start, t->Update, t->LateUpdate, t->FixedUpdate, t->OnDestroy, t->OnGameClose));
    }

    private static string Text(byte* utf8) => utf8 == null ? "" : Marshal.PtrToStringUTF8((nint)utf8) ?? "";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Still in use (Windows): the next compilation's cleanup gets it.
        }
    }

    public void Dispose()
    {
        NativeLibrary.Free(_handle);
        TryDelete(_copy);
    }
}

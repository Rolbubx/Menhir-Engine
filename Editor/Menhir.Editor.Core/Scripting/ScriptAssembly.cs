using System.Reflection;
using System.Runtime.Loader;

namespace Menhir.Editor.Scripting;

/// <summary>
/// The compiled scripts loaded in the editor, in a context of their own so a new compilation can
/// replace them (<see cref="Dispose"/> unloads them). Menhir.Engine is shared with the editor.
/// </summary>
public sealed class ScriptAssembly : IDisposable
{
    private sealed class Context() : AssemblyLoadContext("Menhir scripts", isCollectible: true)
    {
        private static readonly Assembly Engine = typeof(MenhirEngine.Script).Assembly;

        protected override Assembly? Load(AssemblyName name) => name.Name == Engine.GetName().Name ? Engine : null;
    }

    private readonly Context _context;

    private ScriptAssembly(Context context, Assembly assembly)
    {
        _context = context;
        Assembly = assembly;
        ScriptTypes = assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(MenhirEngine.Script)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
    }

    public Assembly Assembly { get; }

    /// <summary>The classes that can be added to entities: non-abstract subclasses of MenhirEngine.Script.</summary>
    public IReadOnlyList<Type> ScriptTypes { get; }

    /// <summary>Loads a compiled assembly (read into memory, so the file can be compiled again).</summary>
    public static ScriptAssembly Load(string path)
    {
        var context = new Context();
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        return new ScriptAssembly(context, context.LoadFromStream(stream));
    }

    public Type? Find(string className) => ScriptTypes.FirstOrDefault(t => t.FullName == className);

    public void Dispose() => _context.Unload();
}

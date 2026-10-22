using System.Diagnostics;
using Menhir.Editor.Build;
using Menhir.Editor.Logging;
using Menhir.Editor.Projects;

namespace Menhir.Editor.Scripting;

/// <summary>The project's compiled scripts: the library Play mode loads, and the script types it has.</summary>
public sealed record CompiledScripts(string LibraryPath, IReadOnlyList<ScriptType> Types)
{
    public ScriptType? Find(string name) => Types.FirstOrDefault(t => t.Name == name);
}

/// <summary>
/// Keeps the project's C scripts compiled for Play mode: compiles them (in the background) when the
/// project opens and whenever a .c or .h file changes, logs the compiler's messages to the Console and
/// reads the script types (for the inspector and Add Component). A project without scripts is compiled
/// too: the runtime's built-in components (Rigidbody, BoxCollider, AudioSource...) are script types.
/// </summary>
public sealed class ScriptManager : IDisposable
{
    private const double PollSeconds = 1.0;

    private readonly string? _engineDirectory = PlayerBuilder.FindEngineDirectory(AppContext.BaseDirectory);
    private Project? _project;
    private Task<(ScriptCompileResult Result, CompiledScripts? Scripts, string? LoadError)>? _task;
    private DateTime? _compiledChange;
    private DateTime _compilingChange;
    private bool _hasScripts;
    private long _lastPoll;
    private Stopwatch _timer = new();

    /// <summary>The latest scripts that compiled, or null when the project has none (or they never compiled).</summary>
    public CompiledScripts? Current { get; private set; }

    public bool IsCompiling => _task != null;

    /// <summary>The last compilation failed: Play mode waits until the errors are fixed.</summary>
    public bool HasErrors { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Current"/> changes.</summary>
    public event Action? Reloaded;

    public void OpenProject(Project project)
    {
        _project = project;
        _compiledChange = null;
        SetCurrent(null);
        HasErrors = false;
        _lastPoll = 0;
        // Copies left by an editor that did not close (see ScriptLibrary.Load).
        string loaded = Path.Combine(project.CacheDirectory, "Scripts", "Loaded");
        if (Directory.Exists(loaded))
        {
            foreach (string file in Directory.EnumerateFiles(loaded))
            {
                try { File.Delete(file); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static string LibraryPath(Project project) =>
        Path.Combine(project.CacheDirectory, "Scripts", "GameScripts" + ScriptCompiler.LibraryExtension);

    /// <summary>Call once per frame on the UI thread.</summary>
    public void Update()
    {
        if (_task is { IsCompleted: true } task)
        {
            _task = null;
            if (task.Exception is { } e)
            {
                Log.Error("Could not compile the scripts.", e.GetBaseException());
                HasErrors = true;
            }
            else
                Report(task.Result);
        }

        long now = Stopwatch.GetTimestamp();
        if (_project == null || _task != null || Stopwatch.GetElapsedTime(_lastPoll, now).TotalSeconds < PollSeconds)
            return;
        _lastPoll = now;
        CompileIfChanged();
    }

    /// <summary>Compiles now when a script changed since the last compilation (e.g. before entering Play mode).</summary>
    public void CompileIfChanged()
    {
        if (_project is not { } project || _task != null)
            return;
        DateTime change = ScriptCompiler.LastChange(project);
        if (change == _compiledChange)
            return;
        if (_engineDirectory is not { } engine)
        {
            Log.Error("Cannot compile the scripts: the engine's Engine/ folder was not found next to the editor.");
            _compiledChange = change;
            HasErrors = true;
            return;
        }

        _compilingChange = change;
        _hasScripts = ScriptCompiler.HasScripts(project);
        if (_hasScripts)
            Log.Info("Compiling scripts...");
        _timer = Stopwatch.StartNew();
        string library = LibraryPath(project);
        _task = Task.Run(() =>
        {
            ScriptCompileResult result = ScriptCompiler.CompileLibrary(project, engine, library);
            if (!result.Success)
                return (result, (CompiledScripts?)null, (string?)null);
            try
            {
                return (result, new CompiledScripts(library, ScriptLibrary.ReadTypes(library)), null);
            }
            catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or InvalidDataException or IOException)
            {
                return (result, null, e.Message);
            }
        });
    }

    private void Report((ScriptCompileResult Result, CompiledScripts? Scripts, string? LoadError) outcome)
    {
        _compiledChange = _compilingChange;
        foreach (ScriptDiagnostic diagnostic in outcome.Result.Diagnostics)
        {
            if (diagnostic.IsError)
                Log.Error(diagnostic.ToString());
            else
                Log.Warning(diagnostic.ToString());
        }
        HasErrors = outcome.Scripts == null;
        if (!outcome.Result.Success && !_hasScripts)
        {
            // Only the built-in components were missed: Play mode works, without them.
            HasErrors = false;
            Log.Warning("The built-in components (Rigidbody, colliders, AudioSource) need a C compiler: " +
                        string.Join(" ", outcome.Result.Errors.Select(e => e.Message)));
            return;
        }
        if (!outcome.Result.Success)
        {
            Log.Error("The scripts have compile errors: fix them to use the latest scripts and to enter Play mode.");
            return;
        }
        if (outcome.LoadError is { } error)
        {
            Log.Error($"The compiled scripts could not be loaded: {error}" +
                      (OperatingSystem.IsWindows() ? " (Windows' Smart App Control blocks libraries it does not know.)" : ""));
            return;
        }
        int count = outcome.Scripts!.Types.Count(t => !ScriptCompiler.BuiltInComponents.Contains(t.Name));
        if (_hasScripts)
            Log.Info($"Compiled scripts in {_timer.Elapsed.TotalSeconds:0.0} s ({count} script{(count == 1 ? "" : "s")})");
        SetCurrent(outcome.Scripts);
    }

    private void SetCurrent(CompiledScripts? scripts)
    {
        if (Current == scripts)
            return;
        Current = scripts;
        Reloaded?.Invoke();
    }

    public void Dispose() => _task?.Wait();
}

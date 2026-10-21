using System.Diagnostics;
using Menhir.Editor.Logging;
using Menhir.Editor.Projects;

namespace Menhir.Editor.Scripting;

/// <summary>
/// Keeps the project's scripts compiled for the editor: compiles them (in the background) when the
/// project opens and whenever a .cs file changes, logs the errors to the Console and loads the result.
/// While <see cref="HoldReload"/> is set (Play mode), a new compilation waits to replace the loaded one.
/// </summary>
public sealed class ScriptManager : IDisposable
{
    private const double PollSeconds = 1.0;

    private Project? _project;
    private Task<ScriptCompileResult>? _task;
    private ScriptCompileResult? _pending;
    private DateTime _compiledChange = DateTime.MinValue;
    private DateTime _compilingChange;
    private long _lastPoll;
    private Stopwatch _timer = new();

    /// <summary>The loaded scripts, or null when the project has none (or they never compiled).</summary>
    public ScriptAssembly? Current { get; private set; }

    public bool IsCompiling => _task != null || _pending != null;

    /// <summary>The last compilation failed: Play mode waits until the errors are fixed.</summary>
    public bool HasErrors { get; private set; }

    /// <summary>Keeps <see cref="Current"/> while it is in use (Play mode); a finished compilation is applied after.</summary>
    public bool HoldReload { get; set; }

    /// <summary>Raised on the UI thread when <see cref="Current"/> changes.</summary>
    public event Action? Reloaded;

    public void OpenProject(Project project)
    {
        _project = project;
        _compiledChange = DateTime.MinValue;
        Unload();
        HasErrors = false;
        _lastPoll = 0;
    }

    /// <summary>Call once per frame on the UI thread.</summary>
    public void Update()
    {
        if (_task is { IsCompleted: true } task)
        {
            _task = null;
            _pending = task.IsCompletedSuccessfully ? task.Result : null;
            if (task.Exception is { } e)
            {
                Log.Error("Could not compile the scripts.", e.GetBaseException());
                HasErrors = true;
            }
            else
                Report(_pending!);
        }

        if (_pending != null && !HoldReload)
        {
            Apply(_pending);
            _pending = null;
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
        bool hasScripts = change != DateTime.MinValue;
        if (!hasScripts)
        {
            if (Current != null || HasErrors)
            {
                // The last script was deleted.
                HasErrors = false;
                _compiledChange = DateTime.MinValue;
                if (!HoldReload)
                    Unload();
            }
            return;
        }
        if (change == _compiledChange)
            return;

        _compilingChange = change;
        string engine = typeof(MenhirEngine.Script).Assembly.Location;
        Log.Info("Compiling scripts...");
        _timer = Stopwatch.StartNew();
        _task = Task.Run(() => ScriptCompiler.CompileForEditor(project, engine));
    }

    private void Report(ScriptCompileResult result)
    {
        _compiledChange = _compilingChange;
        HasErrors = !result.Success;
        foreach (ScriptDiagnostic diagnostic in result.Diagnostics)
        {
            if (diagnostic.IsError)
                Log.Error(diagnostic.ToString());
            else
                Log.Warning(diagnostic.ToString());
        }
        if (!result.Success)
            Log.Error($"Scripts have compile errors: fix them to use the latest scripts and to enter Play mode.");
    }

    private void Apply(ScriptCompileResult result)
    {
        if (!result.Success)
            return;
        ScriptAssembly assembly;
        try
        {
            assembly = ScriptAssembly.Load(result.AssemblyPath!);
        }
        catch (Exception e) when (e is IOException or BadImageFormatException or System.Reflection.ReflectionTypeLoadException)
        {
            Log.Error($"Could not load the compiled scripts: {e.Message}");
            HasErrors = true;
            return;
        }
        Unload();
        Current = assembly;
        int count = assembly.ScriptTypes.Count;
        Log.Info($"Compiled scripts in {_timer.Elapsed.TotalSeconds:0.0} s ({count} script class{(count == 1 ? "" : "es")})");
        Reloaded?.Invoke();
    }

    private void Unload()
    {
        if (Current == null)
            return;
        Current.Dispose();
        Current = null;
        Reloaded?.Invoke();
    }

    public void Dispose()
    {
        _task?.Wait();
        Current?.Dispose();
        Current = null;
    }
}

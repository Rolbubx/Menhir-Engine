using Menhir.Editor.Build;
using Menhir.Editor.Input;
using Menhir.Editor.Logging;
using Menhir.Editor.Runtime;
using Menhir.Editor.Scenes;
using Menhir.Editor.Scripting;
using Menhir.Editor.Serialization;

namespace Menhir.Editor.Playing;

/// <summary>
/// The Play, Pause and Step buttons: runs the game in the editor (a <see cref="PlaySession"/> on copies
/// of the build's scenes, starting with the open one, with the compiled C scripts loaded in the editor).
/// The edited scenes are not touched by the game.
/// </summary>
public sealed class PlayMode : IDisposable
{
    private const float MaxDeltaTime = 0.1f; // like the player: a long pause is not a jump

    private readonly EditorContext _context;
    private readonly ScriptManager _scripts;
    private bool _startWhenCompiled;
    private bool _step;
    private ScriptLibrary? _library;

    public PlayMode(EditorContext context, ScriptManager scripts)
    {
        _context = context;
        _scripts = scripts;
    }

    /// <summary>The running game, or null in Edit mode.</summary>
    public PlaySession? Session { get; private set; }

    public bool IsPlaying => Session != null;

    public bool IsPaused { get; set; }

    /// <summary>Play was pressed while the scripts compile: the game starts when they are ready.</summary>
    public bool IsWaitingForScripts => _startWhenCompiled;

    /// <summary>The scenes the game shows, or null in Edit mode.</summary>
    public IReadOnlyList<Scene>? Scenes => Session?.Scenes;

    public void TogglePlay()
    {
        if (IsPlaying || _startWhenCompiled)
            Stop();
        else
            Play();
    }

    public void Play()
    {
        if (IsPlaying || _context.Project is not { } project)
            return;

        _scripts.CompileIfChanged();
        if (_scripts.IsCompiling)
        {
            _startWhenCompiled = true;
            return;
        }
        _startWhenCompiled = false;
        if (_scripts.HasErrors)
        {
            Log.Error("All compiler errors have to be fixed before entering Play mode.");
            return;
        }

        var warnings = new List<string>();
        List<BuildScene> scenes;
        int first;
        try
        {
            _context.RefreshAssets();
            scenes = GameBuild.LoadPlayScenes(project, _context.ActiveScenePath, _context.ActiveScene.Name,
                SceneSerializer.Serialize(_context.ActiveScene), warnings, out first);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SceneFormatException)
        {
            Log.Error($"Could not enter Play mode: {e.Message}");
            return;
        }
        foreach (string warning in warnings)
            Log.Warning(warning);

        // A fresh copy of the scripts' library for each Play: their globals start as declared.
        if (_scripts.Current is { } compiled)
        {
            try
            {
                _library = ScriptLibrary.Load(compiled.LibraryPath);
            }
            catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or InvalidDataException or IOException)
            {
                Log.Error($"Could not load the scripts: {e.Message}");
                return;
            }
        }
        IsPaused = false;
        Log.Info($"Play mode: {scenes[first].Name}");
        Session = new PlaySession(scenes, first, project.Settings.Input, _library, Write, project.Assets.AssetsDirectory);
    }

    public void Stop()
    {
        _startWhenCompiled = false;
        if (Session == null)
            return;
        Session.Dispose();
        Session = null;
        _library?.Dispose();
        _library = null;
        IsPaused = false;
        Log.Info("Edit mode");
    }

    /// <summary>One frame while paused.</summary>
    public void Step()
    {
        if (IsPlaying)
        {
            IsPaused = true;
            _step = true;
        }
    }

    /// <summary>Runs one frame of the game (call once per editor frame, before drawing it).</summary>
    public void Update(InputFrame input, float deltaTime)
    {
        if (_startWhenCompiled && !_scripts.IsCompiling)
            Play();
        if (Session is not { } session || (IsPaused && !_step))
            return;

        // A step goes as far as a 60 Hz frame, like Unity's.
        float dt = _step ? 1f / 60f : Math.Clamp(deltaTime, 0f, MaxDeltaTime);
        _step = false;
        session.Update(input, dt);
        if (session.QuitRequested)
        {
            Log.Info("Application.Quit was called: back to Edit mode.");
            Stop();
        }
    }

    private static void Write(LogLevel level, string message)
    {
        switch (level)
        {
            case LogLevel.Error:
                Log.Error(message);
                break;
            case LogLevel.Warning:
                Log.Warning(message);
                break;
            default:
                Log.Info(message);
                break;
        }
    }

    public void Dispose() => Stop();
}

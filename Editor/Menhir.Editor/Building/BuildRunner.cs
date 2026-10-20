using System.Diagnostics;
using Menhir.Editor.Build;
using Menhir.Editor.Logging;
using Menhir.Editor.Serialization;

namespace Menhir.Editor.Building;

/// <summary>File > Build: runs <see cref="GameBuild"/> on a background thread, logging to the Console.</summary>
public sealed class BuildRunner
{
    private readonly EditorContext _context;
    private Task? _task;

    public BuildRunner(EditorContext context)
    {
        _context = context;
    }

    public bool IsBuilding => _task is { IsCompleted: false };

    /// <summary>Builds the project (the open scene with its unsaved changes); <paramref name="run"/> starts the game after.</summary>
    public void Start(bool run)
    {
        if (IsBuilding || _context.Project is not { } project)
            return;
        string? engine = PlayerBuilder.FindEngineDirectory(AppContext.BaseDirectory);
        if (engine == null)
        {
            Log.Error("Cannot build: the engine's Engine/Player folder was not found next to the editor.");
            return;
        }

        // Everything the build reads from the editor is taken now, on the UI thread.
        _context.RefreshAssets();
        string? scenePath = _context.ActiveScenePath;
        string sceneJson = SceneSerializer.Serialize(_context.ActiveScene);
        Log.Info($"Building {project.Name}...");
        _task = Task.Run(() =>
        {
            var timer = Stopwatch.StartNew();
            try
            {
                GameBuildResult result = GameBuild.Run(project, engine, scenePath, sceneJson, line => Log.Info($"Build: {line}"));
                foreach (string warning in result.Warnings)
                    Log.Warning($"Build: {warning}");
                Log.Info($"Built {result.Executable} in {timer.Elapsed.TotalSeconds:0.0} s");
                if (run)
                    Process.Start(new ProcessStartInfo(result.Executable) { WorkingDirectory = result.Directory })?.Dispose();
            }
            catch (BuildException e)
            {
                Log.Error($"Build failed: {e.Message}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                Log.Error($"Build failed: {e.Message}", e);
            }
        });
    }
}

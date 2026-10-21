using Menhir.Editor.Assets;
using Menhir.Editor.Assets.Importers;
using Menhir.Editor.Projects;
using Menhir.Editor.Rendering;
using Menhir.Editor.Scenes;
using Menhir.Editor.Serialization;

namespace Menhir.Editor.Build;

/// <summary>What File > Build made: the folder, the executable and what was left out.</summary>
public sealed record GameBuildResult(string Directory, string Executable, IReadOnlyList<string> Warnings);

/// <summary>
/// File > Build: writes Build/&lt;Name&gt;/ in the project with the game's main.c (standalone, see
/// <see cref="PlayerSource"/>), its GameData.binchunk (see <see cref="GameDataExporter"/>) and the
/// executable compiled from them.
/// </summary>
public static class GameBuild
{
    public const string DataFileName = "GameData.binchunk";

    /// <param name="engineDirectory">The repository's Engine/ folder.</param>
    /// <param name="openScenePath">Asset path of the scene open in the editor, or null when it has no file yet.</param>
    /// <param name="openSceneJson">That scene as the editor has it (with unsaved changes), serialized.</param>
    /// <param name="log">Receives a line for each step (from the calling thread).</param>
    public static GameBuildResult Run(Project project, string engineDirectory, string? openScenePath, string? openSceneJson,
        Action<string> log)
    {
        var warnings = new List<string>();
        List<BuildScene> scenes = LoadScenes(project, openScenePath, openSceneJson, warnings);
        if (scenes.Count == 0)
            throw new BuildException("The project has no scene to build. Save the scene first (File > Save Scene As).");

        string name = SafeName(project.Name);
        string directory = Path.Combine(project.BuildDirectory, name);
        Directory.CreateDirectory(directory);

        log($"Exporting {scenes.Count} scene(s) to {DataFileName}");
        byte[] data = GameDataExporter.Export(project.Name, scenes, project.Settings.Rendering, project.Settings.Build,
            guid => LoadTexture(project, guid), guid => LoadModel(project, guid), warnings);
        File.WriteAllBytes(Path.Combine(directory, DataFileName), data);

        log("Writing main.c");
        string mainC = Path.Combine(directory, "main.c");
        File.WriteAllText(mainC, PlayerSource.Amalgamate(engineDirectory));

        string executable = Path.Combine(directory, PlayerBuilder.ExecutableName(name));
        new PlayerBuilder(engineDirectory, project.Settings.Build.Compiler)
            .Build(mainC, executable, Path.Combine(project.CacheDirectory, "Player"), log);
        return new GameBuildResult(directory, executable, warnings);
    }

    /// <summary>
    /// The build settings' scenes, or every scene of the asset database (refreshed by the caller) with
    /// the open one first.
    /// </summary>
    public static List<BuildScene> LoadScenes(Project project, string? openScenePath, string? openSceneJson, List<string> warnings)
    {
        List<string> paths = project.Settings.Build.Scenes.Count > 0
            ? project.Settings.Build.Scenes.ToList()
            : project.Assets.GetAllEntries()
                .Where(e => !e.IsFolder && e.Meta.Importer == AssetImporters.Scene)
                .Select(e => e.Path)
                .OrderBy(p => p != openScenePath)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

        var scenes = new List<BuildScene>();
        foreach (string path in paths.Distinct())
        {
            string json;
            if (path == openScenePath && openSceneJson != null)
                json = openSceneJson;
            else if (File.Exists(project.Assets.ToAbsolutePath(path)))
                json = File.ReadAllText(project.Assets.ToAbsolutePath(path));
            else
            {
                warnings.Add($"The build settings list {path}, which does not exist; it is left out.");
                continue;
            }

            var sceneWarnings = new List<string>();
            Scene scene;
            try
            {
                scene = SceneSerializer.Deserialize(json, sceneWarnings);
            }
            catch (SceneFormatException e)
            {
                warnings.Add($"{path} cannot be read ({e.Message}); it is left out.");
                continue;
            }
            warnings.AddRange(sceneWarnings.Select(w => $"{path}: {w}"));
            scenes.Add(new BuildScene(Path.GetFileNameWithoutExtension(path), scene));
        }
        return scenes;
    }

    private static ImageData? LoadTexture(Project project, Guid guid)
    {
        if (project.Assets.FindByGuid(guid) is not { IsFolder: false } entry)
            return null;
        try
        {
            return PngDecoder.Decode(File.ReadAllBytes(project.Assets.ToAbsolutePath(entry.Path)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    private static MeshData LoadModel(Project project, Guid guid)
    {
        if (project.Assets.FindByGuid(guid) is not { IsFolder: false } entry)
            throw new IOException("the asset does not exist");
        return ModelImporter.Import(project.Assets.ToAbsolutePath(entry.Path));
    }

    /// <summary>The project name as a file name: letters, digits, '-' and '_' (others become '_').</summary>
    public static string SafeName(string name)
    {
        string safe = new(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return safe.Trim('_').Length == 0 ? "Game" : safe;
    }
}

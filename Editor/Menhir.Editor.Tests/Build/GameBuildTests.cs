using System.Text.RegularExpressions;
using Menhir.Editor.Build;
using Menhir.Editor.Projects;
using Menhir.Editor.Scenes;
using Menhir.Editor.Serialization;

namespace Menhir.Editor.Tests.Build;

public sealed class GameBuildTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "menhir-tests", Guid.NewGuid().ToString("N"));

    public GameBuildTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Engine => PlayerBuilder.FindEngineDirectory(AppContext.BaseDirectory)!;

    [Fact]
    public void Amalgamate_GivesOneFileWithoutLocalIncludes()
    {
        string source = PlayerSource.Amalgamate(Engine);

        Assert.DoesNotMatch(new Regex("^\\s*#include \"", RegexOptions.Multiline), source);
        Assert.Contains("#include <GLFW/glfw3.h>", source);
        Assert.Contains("GLAD_GL_IMPLEMENTATION", source);
        Assert.Contains("static const char mh_shader_Shaders_MeshFragment[] =", source);
        Assert.Contains("static const char mh_shader_PostProcessShaders_Uber[] =", source);
        Assert.Contains("int main(int argc, char **argv)", source);
    }

    [Fact]
    public void LoadScenes_PutsTheOpenSceneFirst_WithItsUnsavedChanges()
    {
        Project project = Project.Create(_root, "Game");
        File.WriteAllText(Path.Combine(project.RootPath, "Assets", "Scenes", "A.scene"), SceneSerializer.Serialize(new Scene()));
        project.Assets.Refresh();

        Scene open = SceneTemplates.CreateDefault();
        open.CreateEntity("Unsaved");
        var warnings = new List<string>();
        List<BuildScene> scenes = GameBuild.LoadScenes(project, "Assets/Scenes/Main.scene", SceneSerializer.Serialize(open), warnings);

        Assert.Equal(["Main", "A"], scenes.Select(s => s.Name));
        Assert.Contains(scenes[0].Scene.Entities, e => e.Name == "Unsaved");
        Assert.Empty(warnings);
    }

    [Fact]
    public void LoadScenes_FollowsTheBuildSettings()
    {
        Project project = Project.Create(_root, "Game");
        project.Settings.Build.Scenes = ["Assets/Scenes/Missing.scene", "Assets/Scenes/Main.scene"];
        var warnings = new List<string>();
        List<BuildScene> scenes = GameBuild.LoadScenes(project, null, null, warnings);

        Assert.Equal(["Main"], scenes.Select(s => s.Name));
        Assert.Contains(warnings, w => w.Contains("Missing.scene"));
    }

    [Fact]
    public void Run_WritesMainCTheDataAndTheExecutable()
    {
        Project project = Project.Create(_root, "My Game");
        project.Assets.Refresh();
        var log = new List<string>();
        GameBuildResult result = GameBuild.Run(project, Engine, null, null, log.Add);

        Assert.Equal(Path.Combine(project.RootPath, "Build", "My_Game"), result.Directory);
        Assert.True(File.Exists(Path.Combine(result.Directory, "main.c")));
        Assert.True(File.Exists(result.Executable));
        Assert.Empty(result.Warnings);

        List<Chunk> chunks = BinChunkReader.Read(File.ReadAllBytes(Path.Combine(result.Directory, GameBuild.DataFileName)));
        Assert.Equal(["GAME", "SCNE"], chunks.Select(c => BinChunk.TypeName(c.Type)).Distinct());
        Assert.Contains(log, line => line.Contains("main.c"));
    }

    [Fact]
    public void SafeName_KeepsLettersDigitsDashesAndUnderscores()
    {
        Assert.Equal("My_Game_2", GameBuild.SafeName("My Game 2"));
        Assert.Equal("Game", GameBuild.SafeName("???"));
    }
}

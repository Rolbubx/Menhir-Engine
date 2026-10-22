using Menhir.Editor.Assets;
using Menhir.Editor.Build;
using Menhir.Editor.Projects;
using Menhir.Editor.Scenes;
using Menhir.Editor.Scripting;
using Menhir.Editor.Serialization;

namespace Menhir.Editor.Tests.Scripting;

public sealed class ScriptTemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "menhir-tests", Guid.NewGuid().ToString("N"));
    private readonly Project _project;

    public ScriptTemplateTests()
    {
        Directory.CreateDirectory(_root);
        _project = Project.Create(_root, "Game");
        _project.Assets.Refresh();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Create_PicksAFreeName()
    {
        Assert.Equal("Assets/NewScript.c", ScriptTemplate.Create(_project.Assets, AssetPaths.Root));
        Assert.Equal("Assets/NewScript1.c", ScriptTemplate.Create(_project.Assets, AssetPaths.Root));
        Assert.Contains("MH_CREATE NewScript1.c", File.ReadAllText(_project.Assets.ToAbsolutePath("Assets/NewScript1.c")));
        Assert.Equal(AssetImporters.Script, _project.Assets.FindByPath("Assets/NewScript1.c")!.Meta.Importer);
    }

    [Fact]
    public void RenameClass_FollowsTheFileOnlyForAnUntouchedTemplate()
    {
        string path = _project.Assets.Rename(ScriptTemplate.Create(_project.Assets, AssetPaths.Root), "Player.c");
        string file = _project.Assets.ToAbsolutePath(path);
        Assert.True(ScriptTemplate.RenameClass(file, "NewScript.c", "Player.c"));
        Assert.Equal(ScriptTemplate.Content("Player"), File.ReadAllText(file));

        File.AppendAllText(file, "// edited\n");
        Assert.False(ScriptTemplate.RenameClass(file, "Player.c", "Hero.c"));
        Assert.False(ScriptTemplate.RenameClass(file, "Player.c", "My Hero.c")); // not a C name
        Assert.Contains("MH_CREATE Player.c", File.ReadAllText(file));
    }

    [Fact]
    public void LoadPlayScenes_StartsWithTheOpenScene_EvenOutsideTheBuildList()
    {
        File.WriteAllText(_project.Assets.ToAbsolutePath("Assets/A.scene"), SceneSerializer.Serialize(new Scene()));
        File.WriteAllText(_project.Assets.ToAbsolutePath("Assets/B.scene"), SceneSerializer.Serialize(new Scene()));
        _project.Assets.Refresh();
        string json = SceneSerializer.Serialize(new Scene());

        var warnings = new List<string>();
        List<BuildScene> scenes = GameBuild.LoadPlayScenes(_project, "Assets/B.scene", "B", json, warnings, out int first);
        Assert.Equal(["B", "A", "Main"], scenes.Select(s => s.Name)); // a new project has Assets/Scenes/Main.scene
        Assert.Equal(0, first);

        _project.Settings.Build.Scenes.Add("Assets/A.scene");
        scenes = GameBuild.LoadPlayScenes(_project, "Assets/B.scene", "B", json, warnings, out first);
        Assert.Equal(["A", "B"], scenes.Select(s => s.Name));
        Assert.Equal(1, first);

        scenes = GameBuild.LoadPlayScenes(_project, null, "Untitled", json, warnings, out first);
        Assert.Equal(["A", "Untitled"], scenes.Select(s => s.Name));
        Assert.Equal(1, first);
        Assert.Empty(warnings);
    }
}

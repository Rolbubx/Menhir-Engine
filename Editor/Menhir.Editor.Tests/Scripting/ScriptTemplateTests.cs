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
        Assert.Equal("Assets/NewScript.cs", ScriptTemplate.Create(_project.Assets, AssetPaths.Root));
        Assert.Equal("Assets/NewScript1.cs", ScriptTemplate.Create(_project.Assets, AssetPaths.Root));
        Assert.Contains("public class NewScript1 : Script", File.ReadAllText(_project.Assets.ToAbsolutePath("Assets/NewScript1.cs")));
        Assert.Equal(AssetImporters.Script, _project.Assets.FindByPath("Assets/NewScript1.cs")!.Meta.Importer);
    }

    [Fact]
    public void RenameClass_FollowsTheFileOnlyForAnUntouchedTemplate()
    {
        string path = _project.Assets.Rename(ScriptTemplate.Create(_project.Assets, AssetPaths.Root), "Player.cs");
        string file = _project.Assets.ToAbsolutePath(path);
        Assert.True(ScriptTemplate.RenameClass(file, "NewScript.cs", "Player.cs"));
        Assert.Equal(ScriptTemplate.Content("Player"), File.ReadAllText(file));

        File.AppendAllText(file, "// edited\n");
        Assert.False(ScriptTemplate.RenameClass(file, "Player.cs", "Hero.cs"));
        Assert.False(ScriptTemplate.RenameClass(file, "Player.cs", "My Hero.cs")); // not a class name
        Assert.Contains("class Player", File.ReadAllText(file));
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

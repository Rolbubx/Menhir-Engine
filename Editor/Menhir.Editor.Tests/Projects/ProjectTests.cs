using Menhir.Editor.Projects;
using Menhir.Editor.Serialization;

namespace Menhir.Editor.Tests.Projects;

public sealed class ProjectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "menhir-tests", Guid.NewGuid().ToString("N"));

    public ProjectTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Create_MakesTheFolderLayoutAndAnEmptyScene()
    {
        Project project = Project.Create(_root, "My Game");

        Assert.Equal(Path.Combine(_root, "My Game"), project.RootPath);
        Assert.Equal("My Game", project.Name);
        Assert.True(File.Exists(Path.Combine(project.RootPath, "ProjectSettings", "ProjectSettings.json")));
        Assert.True(Directory.Exists(Path.Combine(project.RootPath, "Cache")));
        Assert.Equal("Cache/\n", File.ReadAllText(Path.Combine(project.RootPath, ".gitignore")));

        string scene = File.ReadAllText(Path.Combine(project.RootPath, "Assets", "Scenes", "Main.scene"));
        Assert.Empty(SceneSerializer.Deserialize(scene, new List<string>()).Entities);
    }

    [Fact]
    public void Open_ReadsTheProjectName()
    {
        Project.Create(_root, "Game");
        Project project = Project.Open(Path.Combine(_root, "Game"));
        project.Settings.Name = "Renamed";
        project.SaveSettings();

        Assert.Equal("Renamed", Project.Open(project.RootPath).Name);
    }

    [Fact]
    public void Create_RefusesInvalidNamesAndNonEmptyFolders()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Taken"));
        File.WriteAllText(Path.Combine(_root, "Taken", "file.txt"), "");

        Assert.Throws<IOException>(() => Project.Create(_root, "Taken"));
        Assert.Throws<ArgumentException>(() => Project.Create(_root, "a/b"));
        Assert.Throws<ArgumentException>(() => Project.Create(_root, " "));
    }

    [Fact]
    public void Open_RefusesFolderWithoutAssets()
    {
        Assert.Throws<DirectoryNotFoundException>(() => Project.Open(_root));
    }

    [Fact]
    public void EditorState_RemembersTheLastScene()
    {
        Project project = Project.Create(_root, "Game");
        Guid scene = Guid.NewGuid();

        project.SaveEditorState(new ProjectEditorState { LastScene = scene });

        Assert.Equal(scene, Project.Open(project.RootPath).LoadEditorState().LastScene);
    }
}

using Menhir.Editor.Projects;
using Menhir.Editor.Serialization;

namespace Menhir.Editor.Tests.Projects;

public sealed class ProjectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "menhir-tests", Guid.NewGuid().ToString("N"));

    public ProjectTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Create_MakesTheFolderLayoutAndADefaultScene()
    {
        Project project = Project.Create(_root, "My Game");

        Assert.Equal(Path.Combine(_root, "My Game"), project.RootPath);
        Assert.Equal("My Game", project.Name);
        Assert.True(File.Exists(Path.Combine(project.RootPath, "ProjectSettings", "ProjectSettings.json")));
        Assert.True(Directory.Exists(Path.Combine(project.RootPath, "Cache")));
        Assert.Equal("Cache/\n", File.ReadAllText(Path.Combine(project.RootPath, ".gitignore")));

        string scene = File.ReadAllText(Path.Combine(project.RootPath, "Assets", "Scenes", "Main.scene"));
        var warnings = new List<string>();
        Assert.Equal(["Directional Light", "Environment", "Global Volume"],
            SceneSerializer.Deserialize(scene, warnings).Entities.Select(e => e.Name));
        Assert.Empty(warnings);
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
    public void Create_DefaultsToTheUniversalPipelineWithoutRemote()
    {
        Project project = Project.Create(_root, "Game");

        Assert.Equal(ProjectTemplates.UniversalPipeline.Id, project.Settings.Type);
        Assert.Null(project.Settings.Remote);
        string json = File.ReadAllText(Path.Combine(project.RootPath, "ProjectSettings", "ProjectSettings.json"));
        Assert.DoesNotContain("remote", json);
    }

    [Fact]
    public void Create_SavesTheRemoteSettings()
    {
        var remote = new ProjectRemoteSettings
        {
            Url = "  http://my-server:8080  ",
            OnOwnerLeave = OwnerLeavePolicy.HandOverToCollaborator,
            AssetDelivery = AssetDelivery.StreamedChunks,
        };
        Project created = Project.Create(_root, "Shared", ProjectTemplates.UniversalPipeline, remote);

        string json = File.ReadAllText(Path.Combine(created.RootPath, "ProjectSettings", "ProjectSettings.json"));
        Assert.Contains("\"onOwnerLeave\": \"HandOverToCollaborator\"", json);

        ProjectRemoteSettings? reopened = Project.Open(created.RootPath).Settings.Remote;
        Assert.NotNull(reopened);
        Assert.Equal("http://my-server:8080", reopened.Url);
        Assert.Equal(OwnerLeavePolicy.HandOverToCollaborator, reopened.OnOwnerLeave);
        Assert.Equal(AssetDelivery.StreamedChunks, reopened.AssetDelivery);
    }

    [Fact]
    public void Create_RefusesAnInvalidRemote()
    {
        Assert.Throws<ArgumentException>(() =>
            Project.Create(_root, "Game", remote: new ProjectRemoteSettings { Url = "ftp://server" }));
        Assert.False(Directory.Exists(Path.Combine(_root, "Game")));
    }

    [Theory]
    [InlineData("http://localhost:8080", true)]
    [InlineData("https://menhir.example.com/projects/42", true)]
    [InlineData("http://", false)]
    [InlineData("ftp://server", false)]
    [InlineData("my-server", false)]
    [InlineData("", false)]
    public void IsValidUrl_AcceptsOnlyHttpUrls(string url, bool valid)
    {
        Assert.Equal(valid, ProjectRemoteSettings.IsValidUrl(url, out string? error));
        Assert.Equal(valid, error == null);
    }

    [Fact]
    public void ReadSettings_DoesNotCreateAnything()
    {
        Project.Create(_root, "Game");
        string root = Path.Combine(_root, "Game");
        Directory.Delete(Path.Combine(root, "Cache"));

        Assert.Equal("Game", Project.ReadSettings(root)?.Name);
        Assert.False(Directory.Exists(Path.Combine(root, "Cache")));
        Assert.Null(Project.ReadSettings(_root));
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

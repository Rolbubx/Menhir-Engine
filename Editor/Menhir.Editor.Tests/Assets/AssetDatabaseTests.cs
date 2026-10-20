using Menhir.Editor.Assets;

namespace Menhir.Editor.Tests.Assets;

public sealed class AssetDatabaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "menhir-tests", Guid.NewGuid().ToString("N"));
    private readonly AssetDatabase _database;

    public AssetDatabaseTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Assets"));
        _database = new AssetDatabase(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Refresh_CreatesMetaForEveryFileAndFolder()
    {
        WriteFile("Assets/Textures/Rock.png");
        WriteFile("Assets/Notes.txt");

        AssetRefreshResult result = _database.Refresh();

        Assert.Equal(["Assets/Notes.txt", "Assets/Textures", "Assets/Textures/Rock.png"], result.CreatedMetas.Order());
        Assert.True(File.Exists(Abs("Assets/Textures.meta")));
        Assert.True(File.Exists(Abs("Assets/Textures/Rock.png.meta")));
        Assert.False(File.Exists(Abs("Assets.meta")));
        Assert.Equal(AssetImporters.Texture, _database.FindByPath("Assets/Textures/Rock.png")!.Meta.Importer);
        Assert.Equal(AssetImporters.Folder, _database.FindByPath("Assets/Textures")!.Meta.Importer);
    }

    [Fact]
    public void Refresh_KeepsGuidsAcrossScansAndInstances()
    {
        WriteFile("Assets/Notes.txt");
        _database.Refresh();
        Guid guid = _database.FindByPath("Assets/Notes.txt")!.Guid;

        AssetRefreshResult second = _database.Refresh();
        var reopened = new AssetDatabase(_root);
        reopened.Refresh();

        Assert.False(second.HasChanges);
        Assert.Equal(guid, _database.FindByPath("Assets/Notes.txt")!.Guid);
        Assert.Equal(guid, reopened.FindByPath("Assets/Notes.txt")!.Guid);
        Assert.Equal("Assets/Notes.txt", reopened.FindByGuid(guid)!.Path);
    }

    [Fact]
    public void Refresh_SkipsIgnoredFiles()
    {
        WriteFile("Assets/.hidden");
        WriteFile("Assets/backup~");
        WriteFile("Assets/scratch.tmp");

        _database.Refresh();

        Assert.Equal(0, _database.Count);
        Assert.Equal(3, Directory.GetFiles(Abs("Assets")).Length); // no .meta written
    }

    [Fact]
    public void Refresh_RemovesMetaOfDeletedAsset()
    {
        WriteFile("Assets/Old.txt");
        _database.Refresh();
        File.Delete(Abs("Assets/Old.txt"));

        AssetRefreshResult result = _database.Refresh();

        Assert.Equal(["Assets/Old.txt.meta"], result.RemovedMetas);
        Assert.False(File.Exists(Abs("Assets/Old.txt.meta")));
    }

    [Fact]
    public void Refresh_GivesCopyANewGuid_AndKeepsTheOriginal()
    {
        WriteFile("Assets/A.txt");
        _database.Refresh();
        Guid original = _database.FindByPath("Assets/A.txt")!.Guid;
        File.Copy(Abs("Assets/A.txt"), Abs("Assets/B.txt"));
        File.Copy(Abs("Assets/A.txt.meta"), Abs("Assets/B.txt.meta"));

        AssetRefreshResult result = _database.Refresh();

        Assert.Equal(["Assets/B.txt"], result.RepairedMetas);
        Assert.Equal(original, _database.FindByPath("Assets/A.txt")!.Guid);
        Guid copy = _database.FindByPath("Assets/B.txt")!.Guid;
        Assert.NotEqual(original, copy);
        Assert.Equal(copy, AssetMeta.Load(Abs("Assets/B.txt.meta"))!.Guid);
    }

    [Fact]
    public void Refresh_ReplacesUnreadableMeta()
    {
        WriteFile("Assets/A.txt");
        File.WriteAllText(Abs("Assets/A.txt.meta"), "{ not json");

        AssetRefreshResult result = _database.Refresh();

        Assert.Equal(["Assets/A.txt"], result.RepairedMetas);
        Assert.NotNull(AssetMeta.Load(Abs("Assets/A.txt.meta")));
    }

    [Fact]
    public void Meta_KeepsUnknownSettingsWhenRewritten()
    {
        string path = Abs("Assets/A.txt.meta");
        File.WriteAllText(path, """
            { "version": 1, "guid": "0123456789abcdef0123456789abcdef", "importer": "Texture", "settings": { "filter": "point" } }
            """);

        AssetMeta.Load(path)!.Save(path);
        AssetMeta meta = AssetMeta.Load(path)!;

        Assert.Equal(Guid.Parse("0123456789abcdef0123456789abcdef"), meta.Guid);
        Assert.Equal("Texture", meta.Importer);
        Assert.Equal("point", (string?)meta.Settings["filter"]);
        Assert.Contains("\"guid\": \"0123456789abcdef0123456789abcdef\"", File.ReadAllText(path));
    }

    [Fact]
    public void GetChildren_ListsFoldersFirstThenFilesByName()
    {
        WriteFile("Assets/b.txt");
        WriteFile("Assets/A.txt");
        WriteFile("Assets/Zeta/z.txt");
        Directory.CreateDirectory(Abs("Assets/Alpha"));

        _database.Refresh();

        Assert.Equal(["Alpha", "Zeta", "A.txt", "b.txt"], _database.GetChildren("Assets").Select(e => e.Name));
        Assert.Equal(["z.txt"], _database.GetChildren("Assets/Zeta").Select(e => e.Name));
    }

    [Fact]
    public void Rename_MovesMetaAndKeepsGuid()
    {
        WriteFile("Assets/Old.txt");
        _database.Refresh();
        Guid guid = _database.FindByPath("Assets/Old.txt")!.Guid;

        string newPath = _database.Rename("Assets/Old.txt", "New.txt");

        Assert.Equal("Assets/New.txt", newPath);
        Assert.Equal(guid, _database.FindByPath(newPath)!.Guid);
        Assert.False(File.Exists(Abs("Assets/Old.txt.meta")));
        Assert.Null(_database.FindByPath("Assets/Old.txt"));
    }

    [Fact]
    public void Rename_ToExistingNameOrInvalidName_Throws()
    {
        WriteFile("Assets/A.txt");
        WriteFile("Assets/B.txt");
        _database.Refresh();

        Assert.Throws<IOException>(() => _database.Rename("Assets/A.txt", "B.txt"));
        Assert.Throws<ArgumentException>(() => _database.Rename("Assets/A.txt", "sub/A.txt"));
        Assert.Throws<ArgumentException>(() => _database.Rename("Assets/A.txt", "  "));
        Assert.Throws<ArgumentException>(() => _database.Rename("Assets/A.txt", "A.meta"));
    }

    [Fact]
    public void Move_FolderKeepsGuidsOfEverythingInside()
    {
        WriteFile("Assets/Props/Crate.obj");
        Directory.CreateDirectory(Abs("Assets/Models"));
        _database.Refresh();
        Guid folder = _database.FindByPath("Assets/Props")!.Guid;
        Guid crate = _database.FindByPath("Assets/Props/Crate.obj")!.Guid;

        string newPath = _database.Move("Assets/Props", "Assets/Models");

        Assert.Equal("Assets/Models/Props", newPath);
        Assert.Equal(folder, _database.FindByPath("Assets/Models/Props")!.Guid);
        Assert.Equal(crate, _database.FindByPath("Assets/Models/Props/Crate.obj")!.Guid);
        Assert.False(File.Exists(Abs("Assets/Props.meta")));
    }

    [Fact]
    public void Move_FolderIntoItself_Throws()
    {
        WriteFile("Assets/Props/Inner/x.txt");
        _database.Refresh();

        Assert.Throws<InvalidOperationException>(() => _database.Move("Assets/Props", "Assets/Props/Inner"));
        Assert.Throws<InvalidOperationException>(() => _database.Move("Assets/Props", "Assets/Props"));
    }

    [Fact]
    public void Delete_RemovesFolderContentAndMetas()
    {
        WriteFile("Assets/Props/Crate.obj");
        _database.Refresh();

        _database.Delete("Assets/Props");

        Assert.False(Directory.Exists(Abs("Assets/Props")));
        Assert.False(File.Exists(Abs("Assets/Props.meta")));
        Assert.Equal(0, _database.Count);
    }

    [Fact]
    public void CreateFolder_PicksAFreeName()
    {
        _database.Refresh();

        string first = _database.CreateFolder("Assets");
        string second = _database.CreateFolder("Assets");

        Assert.Equal("Assets/New Folder", first);
        Assert.Equal("Assets/New Folder 1", second);
        Assert.True(_database.IsFolder(second));
        Assert.True(File.Exists(Abs("Assets/New Folder 1.meta")));
    }

    private string Abs(string assetPath) => Path.Combine(_root, assetPath.Replace('/', Path.DirectorySeparatorChar));

    private void WriteFile(string assetPath)
    {
        string path = Abs(assetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "content");
    }
}

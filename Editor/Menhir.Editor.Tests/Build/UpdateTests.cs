using System.IO.Compression;
using Menhir.Editor.Updates;

namespace Menhir.Editor.Tests.Build;

public sealed class UpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "menhir-tests", Guid.NewGuid().ToString("N"));

    public UpdateTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string LatestRelease = """
        {
          "tag_name": "v0.3.1", "name": "Menhir 0.3.1", "body": "Audio!", "draft": false, "prerelease": false,
          "assets": [
            { "name": "Menhir-Setup-linux-x64.zip", "size": 100, "browser_download_url": "https://example.com/linux.zip" },
            { "name": "Menhir-Setup-win-x64.zip", "size": 200, "url": "https://api.github.com/assets/2", "browser_download_url": "https://example.com/win.zip" }
          ]
        }
        """;

    [Fact]
    public void ARelease_GivesThisPlatformsPackage()
    {
        Release release = UpdateChecker.Parse(LatestRelease, "Menhir-Setup-win-x64.zip")!;
        Assert.Equal((new Version(0, 3, 1), "Menhir 0.3.1", "Audio!", "https://api.github.com/assets/2", 200L),
            (release.Version, release.Name, release.Notes, release.PackageUrl, release.PackageSize));
        Assert.Equal("https://example.com/linux.zip", UpdateChecker.Parse(LatestRelease, "Menhir-Setup-linux-x64.zip")!.PackageUrl);
        Assert.Null(UpdateChecker.Parse(LatestRelease, "Menhir-Setup-osx-arm64.zip"));
        Assert.Null(UpdateChecker.Parse(LatestRelease.Replace("\"prerelease\": false", "\"prerelease\": true"), "Menhir-Setup-win-x64.zip"));
    }

    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("1.4", "1.4.0")]
    [InlineData("v2.0.1-beta", "2.0.1")]
    [InlineData("V3", "3.0.0")]
    public void Tags_ReadAsVersions(string tag, string version) =>
        Assert.Equal(Version.Parse(version), UpdateChecker.ParseVersion(tag));

    [Fact]
    public void NotAVersion_IsNull() => Assert.Null(UpdateChecker.ParseVersion("latest"));

    [Fact]
    public void TheInstallation_IsFoundAboveTheEditor()
    {
        string editor = Path.Combine(_root, "Menhir", "Editor");
        Directory.CreateDirectory(editor);
        Assert.Null(EditorUpdate.InstallationFolder(editor));
        File.WriteAllText(Path.Combine(_root, "Menhir", "install.json"), "{}");
        Assert.Equal(Path.Combine(_root, "Menhir"), EditorUpdate.InstallationFolder(editor));
    }

    [Fact]
    public void APackage_UnpacksToItsInstaller()
    {
        string setupName = OperatingSystem.IsWindows() ? "MenhirSetup.exe" : "MenhirSetup";
        string package = Path.Combine(_root, "package.zip");
        using (ZipArchive zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            zip.CreateEntry($"Menhir-Setup-test/{setupName}");
            zip.CreateEntry("Menhir-Setup-test/Menhir.zip");
        }
        string setup = EditorUpdate.Unpack(package, Path.Combine(_root, "unpacked"));
        Assert.Equal(Path.Combine(_root, "unpacked", "Menhir-Setup-test", setupName), setup);

        var command = EditorUpdate.Command(setup, "/opt/menhir", 1234, "/home/me/Game");
        Assert.Equal(["--update", "--dir", "/opt/menhir", "--wait-pid", "1234", "--relaunch-project", "/home/me/Game"], command.ArgumentList);
    }

    [Fact]
    public void TheWindowsInstaller_RunsSilentlyAndRelaunches()
    {
        Assert.Equal("Menhir-Setup-win-x64.exe", UpdateChecker.PackageNameFor("win-x64"));
        Assert.Equal("Menhir-Setup-linux-x64.zip", UpdateChecker.PackageNameFor("linux-x64"));
        Assert.Equal("Menhir-Setup-win-x64.zip", UpdateChecker.PackageNameFor("win-x64", consoleInstallation: true));

        var command = EditorUpdate.Command(@"C:\Temp\menhir-update-0.3.0\Menhir-Setup-win-x64.exe",
            @"C:\Users\Me\AppData\Local\Programs\Menhir", 1234, @"C:\Games\My Game");
        Assert.Equal(["/SILENT", "/SUPPRESSMSGBOXES", "/NOCANCEL", "/CLOSEAPPLICATIONS", "/NORESTART",
            @"/DIR=C:\Users\Me\AppData\Local\Programs\Menhir", @"/RELAUNCH=C:\Games\My Game"], command.ArgumentList);
    }

    [Fact]
    public void AWindowsInstallation_IsFoundByItsUninstaller()
    {
        string editor = Path.Combine(_root, "Menhir", "Editor");
        Directory.CreateDirectory(editor);
        File.WriteAllText(Path.Combine(_root, "Menhir", "unins000.exe"), "");
        Assert.Equal(Path.Combine(_root, "Menhir"), EditorUpdate.InstallationFolder(editor));
        Assert.False(EditorUpdate.IsConsoleInstallation(Path.Combine(_root, "Menhir")));
    }
}

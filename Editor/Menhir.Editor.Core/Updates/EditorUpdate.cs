using System.Diagnostics;
using System.IO.Compression;

namespace Menhir.Editor.Updates;

/// <summary>
/// Updating an installed editor (File > Update Editor): the release's package is downloaded and unpacked in the
/// temporary folder, then its installer updates the installation once the editor has closed, and starts the
/// editor again on the project that was open (see Installer/README.md).
/// </summary>
public static class EditorUpdate
{
    private const string TempPrefix = "menhir-update-";

    /// <summary>The installation folder (where the installer wrote install.json) above the editor, or null when it runs from the sources.</summary>
    public static string? InstallationFolder(string? start = null)
    {
        for (DirectoryInfo? directory = new(start ?? AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "install.json")) && Directory.Exists(Path.Combine(directory.FullName, "Editor")))
                return directory.FullName;
        }
        return null;
    }

    /// <summary>Downloads and unpacks a release; returns the path of its installer.</summary>
    public static async Task<string> PrepareAsync(UpdateChecker checker, Release release, IProgress<double>? progress, CancellationToken cancel = default)
    {
        string folder = Path.Combine(Path.GetTempPath(), TempPrefix + release.Version);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        string package = Path.Combine(folder, UpdateChecker.PackageName);
        await checker.DownloadAsync(release, package, progress, cancel);
        return Unpack(package, folder);
    }

    /// <summary>Unpacks a package (Menhir-Setup-&lt;platform&gt;/ with the installer and Menhir.zip); returns the installer's path.</summary>
    public static string Unpack(string package, string folder)
    {
        ZipFile.ExtractToDirectory(package, folder, overwriteFiles: true);
        string name = OperatingSystem.IsWindows() ? "MenhirSetup.exe" : "MenhirSetup";
        string setup = Directory.EnumerateFiles(folder, name, SearchOption.AllDirectories).FirstOrDefault()
                       ?? throw new InvalidDataException($"The package has no {name}.");
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(setup)!, "Menhir.zip")))
            throw new InvalidDataException("The package has no Menhir.zip.");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(setup, File.GetUnixFileMode(setup) | UnixFileMode.UserExecute);
        return setup;
    }

    /// <summary>The installer's command line for updating <paramref name="installation"/> after this editor closes.</summary>
    public static ProcessStartInfo Command(string setup, string installation, int editorProcess, string? project)
    {
        // A window of its own, so the progress shows while the editor is gone.
        var start = new ProcessStartInfo(setup) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(setup)! };
        foreach (string argument in new[] { "--update", "--dir", installation, "--wait-pid", editorProcess.ToString() })
            start.ArgumentList.Add(argument);
        if (project != null)
        {
            start.ArgumentList.Add("--relaunch-project");
            start.ArgumentList.Add(project);
        }
        return start;
    }

    /// <summary>Removes what earlier updates left in the temporary folder.</summary>
    public static void CleanUp()
    {
        try
        {
            foreach (string folder in Directory.EnumerateDirectories(Path.GetTempPath(), TempPrefix + "*"))
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

using System.Diagnostics;
using System.IO.Compression;

namespace Menhir.Setup;

/// <summary>The choices of an installation, from the command line or the wizard.</summary>
internal sealed class InstallOptions
{
    public string? Folder { get; set; }
    public string? Compiler { get; set; }
    public string? DotnetRoot { get; set; }
    public bool? StartMenu { get; set; }
    public bool? Desktop { get; set; }
    public bool Launch { get; set; } = true;
    public string? Payload { get; set; }
}

/// <summary>
/// The installation wizard: where, which C compiler, .NET when the editor needs one, shortcuts; then it unpacks
/// Menhir.zip (Editor/, Engine/, Samples/, Docs/...), writes toolchain.json for the editor, and registers
/// Uninstall.exe.
/// </summary>
internal sealed class Installer(InstallOptions options)
{
    public const string ProgramName = "Menhir Engine";

    private static string Version => typeof(Installer).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    private static string DefaultFolder => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Menhir")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "menhir");

    private static string EditorExecutable(string folder) =>
        Path.Combine(folder, "Editor", OperatingSystem.IsWindows() ? "MenhirEditor.exe" : "MenhirEditor");

    public int Run()
    {
        Ui.ShowLogo($"{ProgramName} {Version} - Setup");

        string payload = options.Payload ?? Path.Combine(AppContext.BaseDirectory, "Menhir.zip");
        if (!File.Exists(payload))
        {
            Ui.Error($"The package Menhir.zip was not found next to the installer ({payload}).");
            return 2;
        }
        using ZipArchive archive = ZipFile.OpenRead(payload);
        bool bundledDotnet = archive.Entries.Any(e => e.FullName.Replace('\\', '/') is "Editor/coreclr.dll" or "Editor/libcoreclr.so" or "Editor/libcoreclr.dylib");
        const int steps = 4;

        // ---- 1. Where -------------------------------------------------------------------------
        Ui.Step(1, steps, "Installation folder");
        string? existing = OperatingSystem.IsWindows() ? WindowsIntegration.InstalledFolder() : null;
        if (existing != null)
            Ui.Info($"{ProgramName} is already installed in {existing}: installing again updates it.");
        string folder = AskFolder(existing ?? DefaultFolder);

        // ---- 2. C compiler --------------------------------------------------------------------
        Ui.Step(2, steps, "C compiler");
        Ui.Dim("Menhir compiles your scripts and your games with it. Supported:\n" + Compilers.Supported);
        string compiler = AskCompiler();

        // ---- 3. .NET --------------------------------------------------------------------------
        Ui.Step(3, steps, ".NET runtime (for the editor)");
        string? dotnetRoot = null;
        if (bundledDotnet)
            Ui.Good("The editor carries its own .NET runtime: nothing else to install.");
        else
            dotnetRoot = AskDotnet();

        // ---- 4. Shortcuts ---------------------------------------------------------------------
        Ui.Step(4, steps, "Shortcuts");
        bool startMenu = false, desktop = false;
        if (OperatingSystem.IsWindows())
        {
            startMenu = options.StartMenu ?? Ui.Confirm("Add Menhir Editor to the Start menu?", true);
            desktop = options.Desktop ?? Ui.Confirm("Add a shortcut on the desktop?", false);
        }
        else
            Ui.Dim("Shortcuts are only made on Windows.");

        // ---- Summary --------------------------------------------------------------------------
        Console.WriteLine();
        Ui.Write("  Ready to install\n", ConsoleColor.Yellow);
        Ui.Info($"  Folder    {folder}");
        Ui.Info($"  Compiler  {compiler}");
        Ui.Info($"  .NET      {(bundledDotnet ? "included" : dotnetRoot ?? "not found (the editor will not start)")}");
        if (OperatingSystem.IsWindows())
            Ui.Info($"  Shortcuts {(startMenu ? "Start menu" : "")}{(startMenu && desktop ? ", " : "")}{(desktop ? "desktop" : "")}{(!startMenu && !desktop ? "none" : "")}");
        if (!Ui.Confirm("Install?", true))
        {
            Ui.Info("Nothing was installed.");
            return 1;
        }

        return Install(archive, folder, compiler, dotnetRoot, startMenu, desktop);
    }

    private string AskFolder(string suggestion)
    {
        string? folder = options.Folder;
        while (true)
        {
            folder ??= Ui.Ask("Install Menhir in", suggestion);
            try
            {
                folder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder));
                Directory.CreateDirectory(folder);
                string probe = Path.Combine(folder, ".menhir-write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                bool foreign = Directory.EnumerateFileSystemEntries(folder).Any() && Manifest.Load(folder) == null;
                if (foreign)
                    Ui.Warn("This folder is not empty: Menhir's files go next to what is there.");
                return folder;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Ui.Error($"Cannot install there: {e.Message}" +
                         (e is UnauthorizedAccessException ? " (choose a folder of yours, or run the installer as administrator)" : ""));
                if (options.Folder != null || Ui.AcceptDefaults)
                    throw new InstallException("The installation folder cannot be written.");
                folder = null;
            }
        }
    }

    private string AskCompiler()
    {
        if (options.Compiler != null)
            return CheckCompiler(options.Compiler, allowAnyway: true) ?? options.Compiler;

        Ui.Info("Looking for compilers...");
        List<CompilerCandidate> found = Compilers.Detect();
        if (found.Count == 0)
            Ui.Warn("None found on the PATH or in the usual folders (MSYS2, w64devkit, LLVM...).");
        for (int i = 0; i < found.Count; i++)
        {
            Ui.Write($"    {i + 1}. ", ConsoleColor.Cyan);
            Console.WriteLine(found[i].Command);
            Ui.Dim($"       {found[i].Version}");
        }
        Ui.Dim("Type a number, a full path to gcc / clang (e.g. C:\\msys64\\ucrt64\\bin\\gcc.exe), or a name on the PATH\n" +
               "(gcc, clang, x86_64-w64-mingw32-gcc). Leave it as is to set it later in Project Settings > Build.");
        while (true)
        {
            string fallback = OperatingSystem.IsWindows() ? "gcc" : "cc";
            string answer = Ui.Ask("Compiler", found.Count > 0 ? "1" : fallback);
            string command = int.TryParse(answer, out int index) && index >= 1 && index <= found.Count ? found[index - 1].Command : answer;
            if (CheckCompiler(command, allowAnyway: Ui.AcceptDefaults) is { } accepted)
                return accepted;
            if (Ui.Confirm("Keep it anyway (you can change it later)?", false))
                return command;
        }
    }

    /// <summary>Tries the compiler; returns it when it works (or when accepting anyway), else null.</summary>
    private static string? CheckCompiler(string command, bool allowAnyway)
    {
        Ui.Info($"Trying {command}...");
        if (Compilers.Check(command) is not { } problem)
        {
            Ui.Good($"{Compilers.Version(command)}: compiles and runs C programs.");
            return command;
        }
        Ui.Warn(problem);
        return allowAnyway ? command : null;
    }

    private string? AskDotnet()
    {
        string? found = options.DotnetRoot ?? Dotnet.Find();
        if (found != null && Dotnet.HasRuntime(found))
        {
            Ui.Good($".NET {Dotnet.RequiredVersion}x found in {found}.");
            return found;
        }
        Ui.Warn($"The editor needs the .NET {Dotnet.RequiredVersion}x runtime, which was not found " +
                "(download: https://dotnet.microsoft.com/download).");
        while (true)
        {
            string answer = Ui.Ask("Folder of dotnet (Enter to skip)", "");
            if (answer.Length == 0)
                return null;
            if (Dotnet.HasRuntime(answer))
            {
                Ui.Good($".NET {Dotnet.RequiredVersion}x found in {answer}.");
                return Path.GetFullPath(answer);
            }
            Ui.Error($"No .NET {Dotnet.RequiredVersion}x runtime in {answer} (it should have shared/Microsoft.NETCore.App/10.*).");
        }
    }

    private int Install(ZipArchive archive, string folder, string compiler, string? dotnetRoot, bool startMenu, bool desktop)
    {
        Console.WriteLine();
        Manifest manifest = Manifest.Load(folder) ?? new Manifest();
        // An update replaces the previous files.
        foreach (string entry in manifest.Entries.Where(e => e is "Editor" or "Engine" or "Docs"))
            Delete(Path.Combine(folder, entry));
        manifest.Version = Version;

        var entries = new HashSet<string>(manifest.Entries, StringComparer.OrdinalIgnoreCase);
        int total = archive.Entries.Count, done = 0, lastPercent = -1;
        long bytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string relative = entry.FullName.Replace('\\', '/');
            string top = relative.Split('/')[0];
            string target = Path.GetFullPath(Path.Combine(folder, relative));
            if (!target.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InstallException($"The package has a file outside the installation folder: {relative}");
            // The sample keeps the user's changes when updating.
            bool keep = top == "Samples" && File.Exists(target);
            if (relative.EndsWith('/'))
                Directory.CreateDirectory(target);
            else if (!keep)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                bytes += entry.Length;
            }
            entries.Add(top);
            int percent = ++done * 100 / Math.Max(total, 1);
            if (percent != lastPercent && !Console.IsOutputRedirected)
            {
                lastPercent = percent;
                Console.Write($"\r  Copying files  [{new string('#', percent / 4).PadRight(25, '.')}] {percent,3}%");
            }
        }
        Console.WriteLine($"\r  Copying files  [{new string('#', 25)}] 100%");

        string editor = EditorExecutable(folder);
        if (!OperatingSystem.IsWindows() && File.Exists(editor))
            File.SetUnixFileMode(editor, File.GetUnixFileMode(editor) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

        new ToolchainFile { Compiler = compiler, DotnetRoot = dotnetRoot }.Save(folder);
        entries.Add(ToolchainFile.FileName);
        Ui.Good($"Compiler set: {compiler} (toolchain.json)");

        // The uninstaller is this program.
        string uninstaller = Path.Combine(folder, OperatingSystem.IsWindows() ? "Uninstall.exe" : "uninstall");
        string self = Environment.ProcessPath!;
        if (!string.Equals(Path.GetFullPath(self), uninstaller, StringComparison.OrdinalIgnoreCase))
            File.Copy(self, uninstaller, overwrite: true);
        entries.Add(Path.GetFileName(uninstaller));
        entries.Add(Manifest.FileName);
        manifest.Entries = entries.Order(StringComparer.OrdinalIgnoreCase).ToList();

        if (OperatingSystem.IsWindows())
        {
            WindowsIntegration.Register(folder, Version, editor, uninstaller, bytes / 1024);
            Ui.Good("Registered in Settings > Apps (uninstall it from there, or with Uninstall.exe).");

            foreach (string old in manifest.Shortcuts)
                Delete(old);
            manifest.Shortcuts.Clear();
            foreach ((bool wanted, string shortcut) in new[] { (startMenu, WindowsIntegration.StartMenuShortcut), (desktop, WindowsIntegration.DesktopShortcut) })
            {
                if (!wanted)
                    continue;
                if (WindowsIntegration.CreateShortcut(shortcut, editor, Path.GetDirectoryName(editor)!))
                {
                    manifest.Shortcuts.Add(shortcut);
                    Ui.Good($"Shortcut: {shortcut}");
                }
                else
                    Ui.Warn($"The shortcut {shortcut} could not be made.");
            }

            // A runtime outside Program Files: the editor finds it through DOTNET_ROOT.
            if (dotnetRoot != null && !Dotnet.IsStandardLocation(dotnetRoot) && WindowsIntegration.GetUserVariable("DOTNET_ROOT") != dotnetRoot)
            {
                WindowsIntegration.SetUserVariable("DOTNET_ROOT", dotnetRoot);
                manifest.DotnetRootSet = dotnetRoot;
                Ui.Good($"DOTNET_ROOT set to {dotnetRoot} for your user.");
            }
        }
        manifest.Save(folder);

        Console.WriteLine();
        Ui.Write($"  {ProgramName} is installed in {folder}\n", ConsoleColor.Green);
        Ui.Dim($"Editor: {editor}\nThe Hub lists the sample project (Samples/Sandbox) to start with.");
        if (options.Launch && File.Exists(editor) && !Ui.AcceptDefaults && Ui.Confirm("Start Menhir now?", true))
            Process.Start(new ProcessStartInfo(editor) { WorkingDirectory = Path.GetDirectoryName(editor)!, UseShellExecute = true })?.Dispose();
        return 0;
    }

    public static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Ui.Warn($"Could not remove {path}: {e.Message}");
        }
    }
}

internal sealed class InstallException(string message) : Exception(message);

using Menhir.Setup;

// MenhirSetup [options]       installs Menhir from the Menhir.zip next to it (a wizard; options answer its questions)
// Uninstall [--yes]           the copy in the installation folder: uninstalls it (also: MenhirSetup --uninstall)
//
//   --dir <folder>            installation folder
//   --compiler <path|name>    C compiler (gcc, clang, C:\msys64\ucrt64\bin\gcc.exe...)
//   --dotnet <folder>         .NET folder, when the package's editor does not carry its own runtime
//   --no-start-menu, --desktop, --no-launch
//   --payload <zip>           the package (default: Menhir.zip next to the installer)
//   --yes                     no questions: the defaults for whatever the options leave open

var options = new InstallOptions();
bool uninstall = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").Equals("Uninstall", StringComparison.OrdinalIgnoreCase);
for (int i = 0; i < args.Length; i++)
{
    string? Next() => i + 1 < args.Length ? args[++i] : null;
    switch (args[i])
    {
        case "--uninstall": uninstall = true; break;
        case "--dir": options.Folder = Next(); break;
        case "--compiler": options.Compiler = Next(); break;
        case "--dotnet": options.DotnetRoot = Next(); break;
        case "--payload": options.Payload = Next(); break;
        case "--no-start-menu": options.StartMenu = false; break;
        case "--desktop": options.Desktop = true; break;
        case "--no-launch": options.Launch = false; break;
        case "--yes" or "-y" or "/S": Ui.AcceptDefaults = true; break;
        default:
            Console.Error.WriteLine($"Unknown option {args[i]}. See the top of Installer/Menhir.Setup/Program.cs.");
            return 2;
    }
}

int result;
try
{
    result = uninstall ? Uninstaller.Run(options.Folder) : new Installer(options).Run();
}
catch (Exception e) when (e is InstallException or IOException or UnauthorizedAccessException or InvalidDataException)
{
    Ui.Error(e.Message);
    result = 3;
}
Ui.Pause();
return result;

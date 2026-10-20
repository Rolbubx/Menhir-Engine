using Menhir.Editor.Assets;

namespace Menhir.Editor;

/// <summary>
/// Picks the project to open at startup: "--project &lt;folder&gt;" on the command line,
/// otherwise the Samples/Sandbox project of this repository when the editor is run from it.
/// (Opening and creating projects from the UI comes with the File menu in Phase 6.)
/// </summary>
public static class ProjectLocator
{
    private static readonly string SandboxRelativePath = Path.Combine("Samples", "Sandbox");

    public static string? Find(string[] args)
    {
        int index = Array.IndexOf(args, "--project");
        if (index >= 0 && index + 1 < args.Length)
            return args[index + 1];

        return FindSandbox(Environment.CurrentDirectory) ?? FindSandbox(AppContext.BaseDirectory);
    }

    private static string? FindSandbox(string start)
    {
        for (DirectoryInfo? directory = new(start); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, SandboxRelativePath);
            if (Directory.Exists(Path.Combine(candidate, AssetPaths.Root)))
                return candidate;
        }
        return null;
    }
}

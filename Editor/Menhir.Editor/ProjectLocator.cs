using Menhir.Editor.Projects;

namespace Menhir.Editor;

/// <summary>
/// Picks the project to open at startup: "--project &lt;folder&gt;" on the command line,
/// otherwise the most recent project that still exists, otherwise the Samples/Sandbox
/// project of this repository when the editor is run from it.
/// </summary>
public static class ProjectLocator
{
    private static readonly string SandboxRelativePath = Path.Combine("Samples", "Sandbox");

    public static string? Find(string[] args, IEnumerable<string> recentProjects)
    {
        int index = Array.IndexOf(args, "--project");
        if (index >= 0 && index + 1 < args.Length)
            return args[index + 1];

        return recentProjects.FirstOrDefault(Project.IsProjectFolder)
               ?? FindSandbox(Environment.CurrentDirectory)
               ?? FindSandbox(AppContext.BaseDirectory);
    }

    private static string? FindSandbox(string start)
    {
        for (DirectoryInfo? directory = new(start); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, SandboxRelativePath);
            if (Project.IsProjectFolder(candidate))
                return candidate;
        }
        return null;
    }
}

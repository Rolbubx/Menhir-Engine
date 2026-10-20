using Menhir.Editor.Projects;

namespace Menhir.Editor;

/// <summary>Finds projects to open at startup.</summary>
public static class ProjectLocator
{
    private static readonly string SandboxRelativePath = Path.Combine("Samples", "Sandbox");

    /// <summary>The folder given as "--project &lt;folder&gt;" on the command line, or null.</summary>
    public static string? FromArgs(string[] args)
    {
        int index = Array.IndexOf(args, "--project");
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>The Samples/Sandbox project of this repository when the editor is run from it, or null.</summary>
    public static string? FindSample() =>
        FindSandbox(Environment.CurrentDirectory) ?? FindSandbox(AppContext.BaseDirectory);

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

namespace Menhir.Setup;

/// <summary>
/// The .NET runtime the editor needs when the package does not carry its own (a framework-dependent package):
/// a folder with dotnet and Microsoft.NETCore.App 10.
/// </summary>
internal static class Dotnet
{
    public const string RequiredVersion = "10.";

    /// <summary>Whether the package's editor carries its own runtime (a self-contained publish).</summary>
    public static bool IsBundled(string editorFolder) =>
        File.Exists(Path.Combine(editorFolder, "coreclr.dll")) || File.Exists(Path.Combine(editorFolder, "libcoreclr.so")) ||
        File.Exists(Path.Combine(editorFolder, "libcoreclr.dylib"));

    /// <summary>The usual places: DOTNET_ROOT, the PATH, Program Files.</summary>
    public static string? Find()
    {
        var candidates = new List<string?>
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Compilers.OnPath(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet") is { } onPath ? Path.GetDirectoryName(onPath) : null,
        };
        if (OperatingSystem.IsWindows())
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"));
        else
            candidates.AddRange(["/usr/share/dotnet", "/usr/lib/dotnet", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet")]);
        return candidates.FirstOrDefault(c => c != null && HasRuntime(c));
    }

    /// <summary>The folder holds dotnet with a .NET 10 runtime.</summary>
    public static bool HasRuntime(string folder)
    {
        string runtimes = Path.Combine(folder, "shared", "Microsoft.NETCore.App");
        return Directory.Exists(runtimes) &&
               Directory.EnumerateDirectories(runtimes).Any(d => Path.GetFileName(d).StartsWith(RequiredVersion, StringComparison.Ordinal));
    }

    /// <summary>Whether the system finds this runtime by itself (the editor then needs no DOTNET_ROOT).</summary>
    public static bool IsStandardLocation(string folder) =>
        OperatingSystem.IsWindows() &&
        string.Equals(Path.GetFullPath(folder).TrimEnd('\\'),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"), StringComparison.OrdinalIgnoreCase);
}

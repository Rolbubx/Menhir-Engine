using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Menhir.Editor.Projects;

namespace Menhir.Editor.Scripting;

/// <summary>A compiler message about a script.</summary>
public sealed record ScriptDiagnostic(string File, int Line, int Column, string Message, bool IsError)
{
    public override string ToString() =>
        File.Length > 0 ? $"{File}:{Line}:{Column}: {(IsError ? "error" : "warning")}: {Message}" : Message;
}

public sealed record ScriptCompileResult(bool Success, string? LibraryPath, IReadOnlyList<ScriptDiagnostic> Diagnostics, string Output)
{
    public IEnumerable<ScriptDiagnostic> Errors => Diagnostics.Where(d => d.IsError);
}

/// <summary>
/// The project's scripts are its .c files in Assets/ (see Docs/Scripting.md). For Play mode they are
/// compiled, with the API's menhir_script.c, into a shared library the editor loads (see
/// <see cref="ScriptLibrary"/>), with the compiler of Project Settings → Build; the built game compiles
/// the same files with the player.
/// </summary>
public static partial class ScriptCompiler
{
    public static IEnumerable<string> ScriptFiles(Project project) => Files(project, "*.c");

    public static bool HasScripts(Project project) => ScriptFiles(project).Any();

    /// <summary>The newest write time of the scripts and their headers (to know when to compile again).</summary>
    public static DateTime LastChange(Project project) =>
        Files(project, "*.c").Concat(Files(project, "*.h")).Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue).Max();

    /// <summary>Engine/Scripting/src/menhir_script.c, the part of the API compiled with the scripts.</summary>
    public static string ApiSource(string engineDirectory) => Path.Combine(engineDirectory, "Scripting", "src", "menhir_script.c");

    /// <summary>Engine/Scripting/include: menhir.h and menhir_abi.h.</summary>
    public static string ApiInclude(string engineDirectory) => Path.Combine(engineDirectory, "Scripting", "include");

    public static string LibraryExtension =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so";

    private static IEnumerable<string> Files(Project project, string pattern) =>
        Directory.Exists(project.Assets.AssetsDirectory)
            ? Directory.EnumerateFiles(project.Assets.AssetsDirectory, pattern, SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith('.'))
                .Order(StringComparer.Ordinal)
            : [];

    /// <summary>Compiles the scripts into <paramref name="library"/> (a shared library).</summary>
    /// <param name="engineDirectory">The repository's Engine/ folder.</param>
    public static ScriptCompileResult CompileLibrary(Project project, string engineDirectory, string library)
    {
        string compiler = string.IsNullOrWhiteSpace(project.Settings.Build.Compiler) ? "cc" : project.Settings.Build.Compiler.Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(library))!);
        var arguments = new List<string>
        {
            // The game's flags (PlayerBuilder), so Play mode computes what the game does.
            "-std=gnu11", "-Wall", "-ffp-contract=off", "-O1", "-g", "-shared", "-DMH_BUILD_LIBRARY",
        };
        arguments.AddRange(Build.PlayerBuilder.ScriptFlags(engineDirectory));
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            arguments.Add("-fPIC");
        arguments.AddRange(ScriptFiles(project));
        arguments.AddRange([ApiSource(engineDirectory), "-o", library, "-lm"]);

        var start = new ProcessStartInfo(compiler) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        string output;
        int exitCode;
        try
        {
            using Process process = Process.Start(start) ?? throw new IOException($"{compiler} did not start.");
            Task<string> errors = process.StandardError.ReadToEndAsync();
            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            output += errors.Result;
            exitCode = process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return new ScriptCompileResult(false, null,
                [new ScriptDiagnostic("", 0, 0, $"Could not start the C compiler '{compiler}' ({e.Message}). Install GCC or Clang, " +
                                                "or set the compiler in Project Settings → Build.", true)], "");
        }

        List<ScriptDiagnostic> diagnostics = ParseDiagnostics(output, project.RootPath);
        bool success = exitCode == 0 && File.Exists(library);
        if (!success && !diagnostics.Any(d => d.IsError))
            diagnostics.Add(new ScriptDiagnostic("", 0, 0, "The scripts could not be compiled:\n" + output.Trim(), true));
        return new ScriptCompileResult(success, success ? library : null, diagnostics, output);
    }

    // GCC and Clang: "path/file.c:12:5: error: message" (Windows paths have a drive letter).
    [GeneratedRegex(@"^(?<file>(?:[A-Za-z]:)?[^:]+):(?<line>\d+):(?<column>\d+): (?<kind>fatal error|error|warning): (?<message>.*)$")]
    private static partial Regex DiagnosticLine();

    private static List<ScriptDiagnostic> ParseDiagnostics(string output, string projectRoot)
    {
        var result = new List<ScriptDiagnostic>();
        foreach (string raw in output.Split('\n'))
        {
            Match m = DiagnosticLine().Match(raw.TrimEnd('\r'));
            if (!m.Success)
                continue;
            string file = m.Groups["file"].Value;
            // Relative to the project, like the rest of the editor ("Assets/Scripts/Player.c").
            string full = Path.GetFullPath(file);
            if (full.StartsWith(Path.GetFullPath(projectRoot), StringComparison.Ordinal))
                file = Path.GetRelativePath(projectRoot, full).Replace('\\', '/');
            var diagnostic = new ScriptDiagnostic(file, int.Parse(m.Groups["line"].Value), int.Parse(m.Groups["column"].Value),
                m.Groups["message"].Value, m.Groups["kind"].Value != "warning");
            if (!result.Contains(diagnostic))
                result.Add(diagnostic);
        }
        return result;
    }
}

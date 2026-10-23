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
    public static IEnumerable<string> ScriptFiles(Project project) =>
        Files(project, "*.c").Concat(Files(project, "*.cpp")).Order(StringComparer.Ordinal);

    /// <summary>The runtime's components, which are script types too (menhir_physics.c, menhir_audio.c).</summary>
    public static readonly IReadOnlySet<string> BuiltInComponents =
        new HashSet<string>(StringComparer.Ordinal) { "AudioSource", "BoxCollider", "Rigidbody", "SphereCollider" };

    /// <summary>Physics or Audio for a built-in component, null for a script.</summary>
    public static string? BuiltInCategory(string name) => name switch
    {
        "BoxCollider" or "SphereCollider" or "Rigidbody" => "Physics",
        "AudioSource" => "Audio",
        _ => null,
    };

    public static bool HasScripts(Project project) => ScriptFiles(project).Any();

    /// <summary>The newest write time of the scripts and their headers (to know when to compile again).</summary>
    public static DateTime LastChange(Project project) =>
        Files(project, "*.c").Concat(Files(project, "*.h")).Concat(Files(project, "*.cpp")).Concat(Files(project, "*.hpp"))
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue).Max();

    /// <summary>
    /// Engine/Scripting/src's runtime, compiled with the scripts: menhir_script.c (registry, math, log),
    /// menhir_runtime.c (objects, tweens, timers, coroutines), menhir_physics.c and menhir_audio.c (whose
    /// miniaudio is a separate object, see <see cref="Build.PlayerBuilder.MiniaudioObject"/>).
    /// </summary>
    public static IReadOnlyList<string> ApiSources(string engineDirectory) =>
        new[] { "menhir_script.c", "menhir_runtime.c", "menhir_physics.c", "menhir_audio.c" }
            .Select(f => Path.Combine(engineDirectory, "Scripting", "src", f)).ToList();

    /// <summary>
    /// The files to compile for the project's scripts: those in the simple syntax as their generated copies
    /// (see <see cref="ScriptTransform"/>, written to <paramref name="generatedDirectory"/>), the others as
    /// they are. Errors in the simple syntax are in the diagnostics.
    /// </summary>
    public static (List<string> Files, List<ScriptDiagnostic> Diagnostics) PrepareSources(Project project, string generatedDirectory)
    {
        var files = new List<string>();
        var diagnostics = new List<ScriptDiagnostic>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in ScriptFiles(project))
        {
            string display = Path.GetRelativePath(project.RootPath, file).Replace('\\', '/');
            string source = File.ReadAllText(file);
            if (Build.PlayerBuilder.IsCpp(file))
            {
                // C++: each class deriving from MenhirEngine is a script.
                IReadOnlyList<string> classes = CppScriptTransform.ScriptClasses(source);
                if (classes.Count == 0)
                {
                    files.Add(file);
                    continue;
                }
                bool clash = false;
                foreach (string c in classes)
                {
                    if (names.TryGetValue(c, out string? taken))
                    {
                        diagnostics.Add(new ScriptDiagnostic(display, 1, 1, $"Two scripts are named {c} (the other one is in {taken}): rename one.", true));
                        clash = true;
                    }
                    names[c] = display;
                }
                ScriptTransformResult cpp = CppScriptTransform.Transform(source, Path.GetFullPath(file), display);
                diagnostics.AddRange(cpp.Diagnostics);
                if (clash || cpp.Generated is not { } code)
                    continue;
                Directory.CreateDirectory(generatedDirectory);
                string generatedCpp = Path.Combine(generatedDirectory, Path.GetFileNameWithoutExtension(file) + ".gen.cpp");
                if (!File.Exists(generatedCpp) || File.ReadAllText(generatedCpp) != code)
                    File.WriteAllText(generatedCpp, code);
                files.Add(generatedCpp);
                continue;
            }
            if (!ScriptTransform.IsSimple(source))
            {
                files.Add(file);
                continue;
            }
            string name = Path.GetFileNameWithoutExtension(file);
            if (names.TryGetValue(name, out string? other))
            {
                diagnostics.Add(new ScriptDiagnostic(display, 1, 1, $"Two scripts are named {name} (the other one is {other}): rename one of the files.", true));
                continue;
            }
            names[name] = display;
            ScriptTransformResult result = ScriptTransform.Transform(source, Path.GetFullPath(file), display);
            diagnostics.AddRange(result.Diagnostics);
            if (result.Generated is not { } generated)
                continue;
            Directory.CreateDirectory(generatedDirectory);
            string target = Path.Combine(generatedDirectory, name + ".c");
            if (!File.Exists(target) || File.ReadAllText(target) != generated)
                File.WriteAllText(target, generated);
            files.Add(target);
        }
        return (files, diagnostics);
    }

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

    /// <summary>
    /// Compiles the scripts into <paramref name="library"/> (a shared library), with the runtime and its
    /// built-in components (Rigidbody, colliders, AudioSource), which a project without scripts uses too.
    /// </summary>
    /// <param name="engineDirectory">The repository's Engine/ folder.</param>
    /// <param name="noAudio">Without miniaudio (sounds do nothing), for tests.</param>
    public static ScriptCompileResult CompileLibrary(Project project, string engineDirectory, string library, bool noAudio = false)
    {
        string compiler = Build.Toolchain.ResolveCompiler(project.Settings.Build.Compiler);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(library))!);
        (List<string> scripts, List<ScriptDiagnostic> prepared) =
            PrepareSources(project, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(library))!, "Generated"));
        if (prepared.Any(d => d.IsError))
            return new ScriptCompileResult(false, null, prepared, "");

        bool pic = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string? audio = null;
        if (!noAudio)
        {
            try
            {
                audio = new Build.PlayerBuilder(engineDirectory, project.Settings.Build.Compiler).MiniaudioObject(pic ? ["-O1", "-fPIC"] : ["-O1"]);
            }
            catch (Build.BuildException e)
            {
                return new ScriptCompileResult(false, null, [.. prepared, new ScriptDiagnostic("", 0, 0, e.Message, true)], "");
            }
        }
        // C++ scripts first, each into an object with the C++ compiler.
        string cxx = Build.Toolchain.CxxCompiler(compiler);
        string objects = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(library))!, "Generated", "obj");
        var linked = new List<string>();
        string output = "";
        foreach (string cpp in scripts.Where(Build.PlayerBuilder.IsCpp))
        {
            Directory.CreateDirectory(objects);
            string obj = Path.Combine(objects, Path.GetFileNameWithoutExtension(cpp) + ".o");
            var cppArguments = new List<string>(Build.PlayerBuilder.CppScriptFlags(engineDirectory)) { "-O1", "-g", "-DMH_BUILD_LIBRARY" };
            if (pic)
                cppArguments.Add("-fPIC");
            if (audio == null)
                cppArguments.Add("-DMENHIR_NO_AUDIO");
            cppArguments.AddRange(["-c", cpp, "-o", obj]);
            (int cppExit, string cppOutput, string? cppError) = RunCompiler(cxx, cppArguments);
            if (cppError != null)
                return new ScriptCompileResult(false, null, [.. prepared, new ScriptDiagnostic("", 0, 0, cppError, true)], "");
            output += cppOutput;
            if (cppExit != 0)
            {
                List<ScriptDiagnostic> failed = [.. prepared, .. ParseDiagnostics(output, project.RootPath)];
                if (!failed.Any(d => d.IsError))
                    failed.Add(new ScriptDiagnostic("", 0, 0, "The C++ scripts could not be compiled:\n" + cppOutput.Trim(), true));
                return new ScriptCompileResult(false, null, failed, output);
            }
            linked.Add(obj);
        }
        bool withCpp = linked.Count > 0;

        var arguments = new List<string>
        {
            // The game's flags (PlayerBuilder), so Play mode computes what the game does.
            "-std=gnu11", "-Wall", "-ffp-contract=off", "-O1", "-g", "-shared", "-DMH_BUILD_LIBRARY",
        };
        arguments.AddRange(Build.PlayerBuilder.ScriptFlags(engineDirectory));
        if (pic)
            arguments.Add("-fPIC");
        if (audio == null)
            arguments.Add("-DMENHIR_NO_AUDIO");
        List<string> cSources = [.. scripts.Where(s => !Build.PlayerBuilder.IsCpp(s)), .. ApiSources(engineDirectory)];
        arguments.AddRange(withCpp ? Build.PlayerBuilder.AsC(cSources) : cSources);
        arguments.AddRange(linked);
        if (audio != null)
            arguments.Add(audio);
        arguments.AddRange(["-o", library, "-lm"]);
        if (pic)
            arguments.AddRange(["-lpthread", "-ldl"]);
        if (withCpp)
            arguments.AddRange(Build.PlayerBuilder.CxxLinkFlags);

        string tool = withCpp ? cxx : compiler;
        (int exitCode, string linkOutput, string? startError) = RunCompiler(tool, arguments);
        if (startError != null)
            return new ScriptCompileResult(false, null, [.. prepared, new ScriptDiagnostic("", 0, 0, startError, true)], "");
        output += linkOutput;

        List<ScriptDiagnostic> diagnostics = [.. prepared, .. ParseDiagnostics(output, project.RootPath)];
        bool success = exitCode == 0 && File.Exists(library);
        if (!success && !diagnostics.Any(d => d.IsError))
            diagnostics.Add(new ScriptDiagnostic("", 0, 0, "The scripts could not be compiled:\n" + output.Trim(), true));
        return new ScriptCompileResult(success, success ? library : null, diagnostics, output);
    }

    /// <summary>Runs the compiler: its exit code and output, or why it did not start.</summary>
    private static (int ExitCode, string Output, string? StartError) RunCompiler(string compiler, List<string> arguments)
    {
        var start = new ProcessStartInfo(compiler) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        try
        {
            using Process process = Process.Start(start) ?? throw new IOException($"{compiler} did not start.");
            Task<string> errors = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output + errors.Result, null);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return (-1, "", $"Could not start the compiler '{compiler}' ({e.Message}). Install GCC or Clang (with g++ or clang++ " +
                            "for C++ scripts), or set the compiler in Project Settings → Build.");
        }
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

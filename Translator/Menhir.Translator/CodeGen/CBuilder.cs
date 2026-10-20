using System.Diagnostics;
using Menhir.Translator.TypeSystem;

namespace Menhir.Translator.CodeGen;

/// <summary>Compiles generated C with the runtime (Engine/Runtime) and the collector (Engine/ThirdParty/bdwgc).</summary>
public sealed class CBuilder
{
    /// <param name="engineDirectory">The repository's Engine/ folder.</param>
    public CBuilder(string engineDirectory, string compiler = "cc")
    {
        EngineDirectory = engineDirectory;
        Compiler = compiler;
    }

    public string EngineDirectory { get; }
    public string Compiler { get; }

    public static readonly string[] CompilerFlags =
    [
        "-std=gnu11", "-O1", "-g0",
        // .NET integer overflow wraps around, and the generated code casts between struct pointers.
        "-fwrapv", "-fno-strict-aliasing",
        "-Wno-unused-label", "-Wno-unused-variable", "-Wno-unused-but-set-variable", "-Wno-unused-function",
    ];

    private string RuntimeInclude => Path.Combine(EngineDirectory, "Runtime", "include");
    private string GcDirectory => Path.Combine(EngineDirectory, "ThirdParty", "bdwgc");

    /// <summary>Builds <paramref name="cFile"/> into <paramref name="executable"/>; the collector is compiled once into <paramref name="cacheDirectory"/>.</summary>
    public void Build(string cFile, string executable, string cacheDirectory)
    {
        Directory.CreateDirectory(cacheDirectory);
        string gcObject = Path.Combine(cacheDirectory, "gc.o");
        if (!File.Exists(gcObject))
        {
            string partial = gcObject + ".tmp";
            Run(["-O2", "-c", Path.Combine(GcDirectory, "extra", "gc.c"), "-I", Path.Combine(GcDirectory, "include"), "-o", partial]);
            File.Move(partial, gcObject, overwrite: true);
        }

        var arguments = new List<string>(CompilerFlags)
        {
            "-I", RuntimeInclude, "-I", Path.Combine(GcDirectory, "include"),
            cFile, Path.Combine(EngineDirectory, "Runtime", "src", "runtime.c"), gcObject,
            "-o", executable, "-lm", "-lpthread",
        };
        Run(arguments);
    }

    private void Run(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Compiler) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new TranslationException($"Could not start {Compiler}.");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new TranslationException($"{Compiler} failed:\n{output}");
    }

    /// <summary>Engine/ found by walking up from <paramref name="start"/>, or null.</summary>
    public static string? FindEngineDirectory(string start)
    {
        for (DirectoryInfo? directory = new(start); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "Engine");
            if (File.Exists(Path.Combine(candidate, "Runtime", "src", "runtime.c")))
                return candidate;
        }
        return null;
    }
}

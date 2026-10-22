using System.Diagnostics;

namespace Menhir.Setup;

/// <summary>A C compiler found on the machine: what to call it, and what it says it is.</summary>
internal sealed record CompilerCandidate(string Command, string Version);

/// <summary>
/// The C compilers Menhir works with: GCC and Clang, which on Windows means MinGW-w64 builds (MSYS2, WinLibs,
/// w64devkit, TDM-GCC, LLVM-MinGW). Microsoft's cl.exe is not one: the scripts and the player use GCC/Clang
/// extensions and options.
/// </summary>
internal static class Compilers
{
    public const string Supported =
        "GCC or Clang (on Windows: MinGW-w64 builds, e.g. MSYS2 (UCRT64, MINGW64, CLANG64), WinLibs,\n" +
        "w64devkit, TDM-GCC, LLVM-MinGW). Not MSVC (cl.exe): Menhir needs GCC/Clang features.";

    private static readonly string[] Names = ["gcc", "clang", "cc", "x86_64-w64-mingw32-gcc"];

    /// <summary>Folders where Windows compilers usually live.</summary>
    private static IEnumerable<string> KnownFolders()
    {
        if (!OperatingSystem.IsWindows())
            return [];
        string[] drives = ["C:\\", "D:\\"];
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var folders = new List<string>();
        foreach (string drive in drives)
        {
            folders.AddRange([
                Path.Combine(drive, "msys64", "ucrt64", "bin"), Path.Combine(drive, "msys64", "mingw64", "bin"),
                Path.Combine(drive, "msys64", "clang64", "bin"), Path.Combine(drive, "w64devkit", "bin"),
                Path.Combine(drive, "mingw64", "bin"), Path.Combine(drive, "TDM-GCC-64", "bin"),
                Path.Combine(drive, "llvm-mingw", "bin"),
            ]);
        }
        folders.AddRange([
            Path.Combine(user, "scoop", "apps", "gcc", "current", "bin"),
            Path.Combine(user, "scoop", "apps", "mingw", "current", "bin"),
            Path.Combine(user, "w64devkit", "bin"),
            Path.Combine(programFiles, "LLVM", "bin"),
        ]);
        return folders;
    }

    /// <summary>The compilers on the PATH, then in the usual folders, that answer --version.</summary>
    public static List<CompilerCandidate> Detect()
    {
        string exe = OperatingSystem.IsWindows() ? ".exe" : "";
        // A name on the PATH first (it keeps working when the compiler is updated), each program once.
        var commands = new List<(string Command, string Path)>();
        foreach (string name in Names)
        {
            if (OnPath(name + exe) is { } path)
                commands.Add((name, path));
        }
        foreach (string folder in KnownFolders())
        {
            foreach (string name in new[] { "gcc", "clang" })
            {
                string path = Path.Combine(folder, name + exe);
                if (File.Exists(path))
                    commands.Add((path, path));
            }
        }

        var found = new List<CompilerCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string command, string path) in commands)
        {
            if (seen.Add(Path.GetFullPath(path)) && Version(command) is { } version)
                found.Add(new CompilerCandidate(command, version));
        }
        return found;
    }

    /// <summary>The first line of "--version", or null when it does not run.</summary>
    public static string? Version(string command)
    {
        (int exit, string output) = Run(command, ["--version"], null, 10_000);
        return exit == 0 ? output.Split('\n')[0].Trim() : null;
    }

    /// <summary>
    /// Compiles and runs a small C program with what Menhir uses (C11, __attribute__((constructor))).
    /// Returns null when it works, else what went wrong.
    /// </summary>
    public static string? Check(string command)
    {
        if (Version(command) == null)
            return $"'{command}' does not run (not found, or not a compiler).";
        if (Path.GetFileNameWithoutExtension(command).Equals("cl", StringComparison.OrdinalIgnoreCase))
            return "MSVC (cl.exe) is not supported: choose GCC or Clang.";

        string folder = Path.Combine(Path.GetTempPath(), "menhir-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string source = Path.Combine(folder, "check.c");
            string program = Path.Combine(folder, OperatingSystem.IsWindows() ? "check.exe" : "check");
            File.WriteAllText(source, """
                #include <math.h>
                #include <stdio.h>
                static int ready;
                static void setup(void) __attribute__((constructor));
                static void setup(void) { ready = 1; }
                int main(void) { printf("%d %.1f\n", ready, sqrt(4.0)); return 0; }
                """);
            (int exit, string output) = Run(command, ["-std=gnu11", "-O1", source, "-o", program, "-lm"], folder, 60_000);
            if (exit != 0)
                return "It does not compile a C file:\n" + output.Trim();
            (exit, output) = Run(program, [], folder, 10_000);
            return exit == 0 && output.Trim() == "1 2.0" ? null : "The program it compiled does not run correctly.";
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    public static string? OnPath(string fileName)
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (folder.Length == 0)
                continue;
            try
            {
                string candidate = Path.Combine(folder.Trim('"'), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry.
            }
        }
        return null;
    }

    public static (int ExitCode, string Output) Run(string command, string[] arguments, string? workingDirectory, int timeoutMs)
    {
        var start = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (workingDirectory != null)
            start.WorkingDirectory = workingDirectory;
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        try
        {
            using Process process = Process.Start(start)!;
            Task<string> errors = process.StandardError.ReadToEndAsync();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill(entireProcessTree: true);
                return (-1, "timed out");
            }
            return (process.ExitCode, output.Result + errors.Result);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (-1, e.Message);
        }
    }
}

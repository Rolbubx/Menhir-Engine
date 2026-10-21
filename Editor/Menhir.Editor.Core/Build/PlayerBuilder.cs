using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Menhir.Editor.Build;

/// <summary>A compiler run that failed, with its output.</summary>
public sealed class BuildException(string message) : Exception(message);

/// <summary>
/// Compiles the game's main.c with GCC or Clang, against GLFW (Engine/ThirdParty/glfw), which is
/// compiled once into a static library and kept in the cache folder. Linux builds use X11 (GLFW
/// loads the X11 libraries when the game starts, so only their development headers are needed to
/// build). The Windows (MinGW) and macOS settings follow GLFW's build files but are untested.
/// A game with scripts also links their C (from IL2C) with the runtime (Engine/Runtime) and the
/// collector (Engine/ThirdParty/bdwgc, compiled once and cached like GLFW).
/// </summary>
public sealed class PlayerBuilder
{
    /// <param name="engineDirectory">The repository's Engine/ folder.</param>
    public PlayerBuilder(string engineDirectory, string compiler = "cc")
    {
        EngineDirectory = engineDirectory;
        Compiler = string.IsNullOrWhiteSpace(compiler) ? "cc" : compiler.Trim();
    }

    public string EngineDirectory { get; }
    public string Compiler { get; }

    private string GlfwDirectory => Path.Combine(EngineDirectory, "ThirdParty", "glfw");
    private string GcDirectory => Path.Combine(EngineDirectory, "ThirdParty", "bdwgc");
    private string RuntimeInclude => Path.Combine(EngineDirectory, "Runtime", "include");

    // No fused multiply-adds, in the player as in the scripts: float results then match the editor's.
    private static readonly string[] PlayerFlags = ["-std=c99", "-Wall", "-ffp-contract=off"];

    public static string ExecutableName(string name) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? name + ".exe" : name;

    /// <summary>Engine/ found by walking up from <paramref name="start"/> (the editor runs from the repository), or null.</summary>
    public static string? FindEngineDirectory(string start)
    {
        for (DirectoryInfo? directory = new(start); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "Engine");
            if (File.Exists(Path.Combine(candidate, "Player", "src", "main.c")))
                return candidate;
        }
        return null;
    }

    /// <summary>Compiles <paramref name="mainC"/> into <paramref name="executable"/>.</summary>
    /// <param name="scriptsC">The scripts' C from IL2C, or null for a game without scripts.</param>
    /// <param name="log">Receives a line for each step.</param>
    public void Build(string mainC, string executable, string cacheDirectory, Action<string>? log = null, string? scriptsC = null)
    {
        string glfw = GlfwLibrary(cacheDirectory, log);
        List<string> scripts = ScriptObjects(scriptsC, cacheDirectory, log);
        log?.Invoke($"Compiling {Path.GetFileName(mainC)}");
        var arguments = new List<string>(PlayerFlags) { "-O2", mainC, "-I", Path.Combine(GlfwDirectory, "include") };
        arguments.AddRange(scripts);
        arguments.AddRange([glfw, "-o", executable]);
        arguments.AddRange(Platform.LinkFlags);
        Run(arguments);
    }

    /// <summary>Compiles the player without OpenGL or a window (only <c>--dump</c> and <c>--simulate</c> work), for tests.</summary>
    public void BuildHeadless(string mainC, string executable, string? scriptsC = null, string? cacheDirectory = null)
    {
        List<string> scripts = ScriptObjects(scriptsC, cacheDirectory ?? Path.GetDirectoryName(Path.GetFullPath(executable))!, null);
        Run([.. PlayerFlags, "-O1", "-DMENHIR_HEADLESS", mainC, .. scripts, "-o", executable, "-lm", "-lpthread"]);
    }

    /// <summary>
    /// For a game with scripts: the flags and objects main.c is compiled with (MENHIR_SCRIPTS, the
    /// scripts, the runtime and the collector); nothing without.
    /// </summary>
    private List<string> ScriptObjects(string? scriptsC, string cacheDirectory, Action<string>? log)
    {
        if (scriptsC == null)
            return [];
        string gc = GcObject(cacheDirectory, log);
        Directory.CreateDirectory(cacheDirectory);
        string scriptsObject = Path.Combine(cacheDirectory, Path.GetFileNameWithoutExtension(scriptsC) + ".o");
        string runtimeObject = Path.Combine(cacheDirectory, "runtime.o");
        log?.Invoke($"Compiling {Path.GetFileName(scriptsC)}");
        // The generated code's own flags (wrap-around integers, struct pointer casts...).
        string[] generated = [.. Menhir.Translator.CodeGen.CBuilder.CompilerFlags, "-I", RuntimeInclude, "-I", Path.Combine(GcDirectory, "include")];
        Run([.. generated, "-c", scriptsC, "-o", scriptsObject]);
        Run([.. generated, "-c", Path.Combine(EngineDirectory, "Runtime", "src", "runtime.c"), "-o", runtimeObject]);
        return ["-DMENHIR_SCRIPTS", "-I", RuntimeInclude, scriptsObject, runtimeObject, gc];
    }

    private string GcObject(string cacheDirectory, Action<string>? log)
    {
        string directory = Path.Combine(cacheDirectory, $"bdwgc-8.2.8-{Platform.Name}");
        string gc = Path.Combine(directory, "gc.o");
        if (File.Exists(gc))
            return gc;
        log?.Invoke("Compiling the garbage collector (once, then kept in the cache)");
        Directory.CreateDirectory(directory);
        string partial = Path.Combine(directory, "gc.tmp.o");
        Run(["-O2", "-c", Path.Combine(GcDirectory, "extra", "gc.c"), "-I", Path.Combine(GcDirectory, "include"), "-o", partial]);
        File.Move(partial, gc, overwrite: true);
        return gc;
    }

    private string GlfwLibrary(string cacheDirectory, Action<string>? log)
    {
        string directory = Path.Combine(cacheDirectory, $"glfw-3.4-{Platform.Name}");
        string library = Path.Combine(directory, "libglfw3.a");
        if (File.Exists(library))
            return library;

        log?.Invoke("Compiling GLFW (once, then kept in the cache)");
        Directory.CreateDirectory(directory);
        var objects = new List<string>();
        foreach (string file in Platform.GlfwSources)
        {
            string obj = Path.Combine(directory, Path.GetFileNameWithoutExtension(file) + ".o");
            var arguments = new List<string> { "-c", "-O2", Path.Combine(GlfwDirectory, "src", file), "-o", obj };
            arguments.AddRange(Platform.GlfwDefines);
            Run(arguments);
            objects.Add(obj);
        }
        string partial = library + ".tmp";
        File.Delete(partial);
        RunTool("ar", ["rcs", partial, .. objects]);
        File.Move(partial, library, overwrite: true);
        return library;
    }

    private void Run(IEnumerable<string> arguments) => RunTool(Compiler, arguments);

    private static void RunTool(string tool, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(tool) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new BuildException($"Could not start {tool}.");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new BuildException($"Could not start {tool} ({e.Message}). Install GCC or Clang, or set the compiler in " +
                                     "Project Settings > Build.");
        }
        using (process)
        {
            Task<string> error = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            output += error.Result;
            if (process.ExitCode != 0)
                throw new BuildException($"{tool} failed:\n{output.Trim()}");
        }
    }

    /// <summary>GLFW's files and flags per platform (from its src/CMakeLists.txt).</summary>
    private static class Platform
    {
        private static readonly string[] Common =
            ["context.c", "init.c", "input.c", "monitor.c", "platform.c", "vulkan.c", "window.c", "egl_context.c",
             "osmesa_context.c", "null_init.c", "null_monitor.c", "null_window.c", "null_joystick.c"];

        public static string Name =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : "linux-x11";

        public static string[] GlfwSources => Name switch
        {
            "windows" => [.. Common, "win32_module.c", "win32_time.c", "win32_thread.c", "win32_init.c", "win32_joystick.c",
                          "win32_monitor.c", "win32_window.c", "wgl_context.c"],
            "macos" => [.. Common, "cocoa_time.c", "posix_module.c", "posix_thread.c", "cocoa_init.m", "cocoa_joystick.m",
                        "cocoa_monitor.m", "cocoa_window.m", "nsgl_context.m"],
            _ => [.. Common, "posix_module.c", "posix_time.c", "posix_thread.c", "posix_poll.c", "x11_init.c",
                  "x11_monitor.c", "x11_window.c", "xkb_unicode.c", "glx_context.c", "linux_joystick.c"],
        };

        public static string[] GlfwDefines => Name switch
        {
            "windows" => ["-D_GLFW_WIN32", "-DUNICODE", "-D_UNICODE"],
            "macos" => ["-D_GLFW_COCOA"],
            _ => ["-D_GLFW_X11", "-D_DEFAULT_SOURCE"],
        };

        public static string[] LinkFlags => Name switch
        {
            "windows" => ["-lgdi32", "-mwindows"],
            "macos" => ["-framework", "Cocoa", "-framework", "IOKit", "-framework", "CoreFoundation", "-framework", "QuartzCore"],
            _ => ["-lm", "-ldl", "-lpthread"],
        };
    }
}

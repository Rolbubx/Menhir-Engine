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
/// A game with scripts compiles their .c files with it, and the API's menhir_script.c (Engine/Scripting).
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
    private string ScriptingInclude => Path.Combine(EngineDirectory, "Scripting", "include");

    // No fused multiply-adds: float results then match the editor's (and the scripts' in Play mode).
    private static readonly string[] PlayerFlags = ["-std=gnu11", "-Wall", "-ffp-contract=off"];

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
    /// <param name="scripts">The game's scripts (.c files, with menhir_script.c), or null for a game without scripts.</param>
    /// <param name="log">Receives a line for each step.</param>
    public void Build(string mainC, string executable, string cacheDirectory, Action<string>? log = null,
        IReadOnlyList<string>? scripts = null)
    {
        string glfw = GlfwLibrary(cacheDirectory, log);
        log?.Invoke(scripts == null ? $"Compiling {Path.GetFileName(mainC)}" : $"Compiling {Path.GetFileName(mainC)} and {scripts.Count} script file(s)");
        var arguments = new List<string>(PlayerFlags) { "-O2", mainC, "-I", Path.Combine(GlfwDirectory, "include") };
        arguments.AddRange(ScriptArguments(scripts, Path.Combine(cacheDirectory, "Scripts")));
        arguments.AddRange([glfw, "-o", executable]);
        arguments.AddRange(Platform.LinkFlags);
        Run(arguments);
    }

    /// <summary>Compiles the player without OpenGL or a window (only <c>--dump</c> and <c>--simulate</c> work), for tests.</summary>
    public void BuildHeadless(string mainC, string executable, IReadOnlyList<string>? scripts = null) =>
        Run([.. PlayerFlags, "-O1", "-DMENHIR_HEADLESS", mainC, .. ScriptArguments(scripts, executable + "-scripts"),
             "-o", executable, "-lm"]);

    /// <summary>
    /// With scripts: MENHIR_SCRIPTS and the API's headers for main.c, and the scripts compiled on their own
    /// into <paramref name="objectDirectory"/>, with menhir.h included first (see <see cref="ScriptFlags"/>).
    /// </summary>
    private List<string> ScriptArguments(IReadOnlyList<string>? scripts, string objectDirectory)
    {
        if (scripts == null)
            return [];
        Directory.CreateDirectory(objectDirectory);
        var arguments = new List<string> { "-DMENHIR_SCRIPTS", "-I", ScriptingInclude };
        for (int i = 0; i < scripts.Count; i++)
        {
            string obj = Path.Combine(objectDirectory, $"{i}-{Path.GetFileNameWithoutExtension(scripts[i])}.o");
            Run([.. PlayerFlags, .. ScriptFlags(EngineDirectory), "-O2", "-c", scripts[i], "-o", obj]);
            arguments.Add(obj);
        }
        return arguments;
    }

    /// <summary>
    /// How scripts are compiled, for the game and for Play mode: menhir.h is included before each file,
    /// so a script does not need its own #include &lt;menhir.h&gt;.
    /// </summary>
    public static string[] ScriptFlags(string engineDirectory)
    {
        string include = Path.Combine(engineDirectory, "Scripting", "include");
        return ["-I", include, "-include", Path.Combine(include, "menhir.h")];
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

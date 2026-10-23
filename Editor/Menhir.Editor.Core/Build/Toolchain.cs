using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Menhir.Editor.Build;

/// <summary>
/// The C compiler the editor uses for the scripts and the built games, unless a project sets its own
/// (Project Settings → Build). The installer writes it to toolchain.json in the installation folder (next to
/// Engine/); without that file, it is gcc on Windows and cc elsewhere, found on the PATH.
/// </summary>
public sealed class Toolchain
{
    public const string FileName = "toolchain.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static Toolchain? _current;

    /// <summary>A path to gcc or clang, or a name found on the PATH ("gcc", "x86_64-w64-mingw32-gcc"...).</summary>
    public string Compiler { get; set; } = DefaultCompiler;

    /// <summary>Where the installer found .NET for the editor (only when the editor does not carry its own).</summary>
    public string? DotnetRoot { get; set; }

    public static string DefaultCompiler => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "gcc" : "cc";

    /// <summary>The editor's toolchain: toolchain.json found above the editor's folder, or the defaults.</summary>
    public static Toolchain Current => _current ??= Load(Find(AppContext.BaseDirectory));

    /// <summary>The compiler to use for a project: its own when it sets one, else the editor's.</summary>
    public static string ResolveCompiler(string? projectCompiler) =>
        string.IsNullOrWhiteSpace(projectCompiler) ? Current.Compiler : projectCompiler.Trim();

    /// <summary>toolchain.json in <paramref name="start"/> or a folder above it, or null.</summary>
    public static string? Find(string start)
    {
        for (DirectoryInfo? directory = new(start); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, FileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    public static Toolchain Load(string? path)
    {
        if (path == null)
            return new Toolchain();
        try
        {
            Toolchain loaded = JsonSerializer.Deserialize<Toolchain>(File.ReadAllText(path), Json) ?? new Toolchain();
            if (string.IsNullOrWhiteSpace(loaded.Compiler))
                loaded.Compiler = DefaultCompiler;
            return loaded;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Logging.Log.Warning($"{path} cannot be read ({e.Message}); using {DefaultCompiler}.");
            return new Toolchain();
        }
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));

    /// <summary>
    /// The archiver that goes with a compiler: ar (or llvm-ar for Clang) in the compiler's folder, else on the
    /// PATH; null when there is none.
    /// </summary>
    public static string? FindArchiver(string compiler)
    {
        string exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
        string name = Path.GetFileNameWithoutExtension(compiler);
        // "x86_64-w64-mingw32-gcc" goes with "x86_64-w64-mingw32-ar".
        string prefix = name.EndsWith("gcc", StringComparison.OrdinalIgnoreCase) ? name[..^3] : "";
        string[] names = name.Contains("clang", StringComparison.OrdinalIgnoreCase)
            ? ["llvm-ar", "ar"]
            : [prefix + "ar", "ar"];
        string? folder = Path.GetDirectoryName(compiler);
        foreach (string candidate in names)
        {
            if (!string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, candidate + exe)))
                return Path.Combine(folder, candidate + exe);
            if (OnPath(candidate + exe) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>
    /// The C++ compiler that goes with a C compiler, for C++ scripts: g++ for gcc ("x86_64-w64-mingw32-g++"...),
    /// clang++ for clang, c++ for cc, in the same folder. The C compiler itself when there is no such one.
    /// </summary>
    public static string CxxCompiler(string compiler)
    {
        string exe = Path.GetExtension(compiler);
        string name = Path.GetFileNameWithoutExtension(compiler);
        string? cxx = null;
        foreach ((string c, string cpp) in new[] { ("gcc", "g++"), ("clang", "clang++"), ("cc", "c++") })
        {
            int at = name.LastIndexOf(c, StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && !name.Contains(cpp, StringComparison.OrdinalIgnoreCase))
            {
                cxx = name[..at] + cpp + name[(at + c.Length)..];
                break;
            }
        }
        if (cxx == null)
            return compiler;
        string? folder = Path.GetDirectoryName(compiler);
        if (string.IsNullOrEmpty(folder))
            return OnPath(cxx + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "")) != null ? cxx : compiler;
        string candidate = Path.Combine(folder, cxx + exe);
        return File.Exists(candidate) ? candidate : compiler;
    }

    /// <summary>A program found in a folder of the PATH, or null.</summary>
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
}

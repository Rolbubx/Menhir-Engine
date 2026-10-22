using System.Text.Json;

namespace Menhir.Setup;

/// <summary>
/// install.json in the installation folder: what the installer put there and changed, so the uninstaller
/// removes exactly that (projects saved inside the folder by the user stay).
/// </summary>
internal sealed class Manifest
{
    public const string FileName = "install.json";

    public string Version { get; set; } = "";
    /// <summary>Files and folders of the installation folder, relative to it.</summary>
    public List<string> Entries { get; set; } = [];
    public List<string> Shortcuts { get; set; } = [];
    /// <summary>DOTNET_ROOT was set for the user by the installer (and is removed with it).</summary>
    public string? DotnetRootSet { get; set; }

    public static Manifest? Load(string folder)
    {
        string path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), ManifestJson.Default.Manifest);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(string folder) =>
        File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, ManifestJson.Default.Manifest));
}

/// <summary>The toolchain.json the editor reads (Editor/Menhir.Editor.Core/Build/Toolchain.cs).</summary>
internal sealed class ToolchainFile
{
    public const string FileName = "toolchain.json";

    public string Compiler { get; set; } = "";
    public string? DotnetRoot { get; set; }

    public static ToolchainFile? Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), ManifestJson.Default.ToolchainFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(string folder) =>
        File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, ManifestJson.Default.ToolchainFile));
}

// Source-generated serialization: the installer is published trimmed.
[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true,
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
[System.Text.Json.Serialization.JsonSerializable(typeof(Manifest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(ToolchainFile))]
internal sealed partial class ManifestJson : System.Text.Json.Serialization.JsonSerializerContext;

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Menhir.Editor.Assets;

/// <summary>
/// Content of a ".meta" file stored next to every asset (and folder) in Assets/.
/// The GUID is the asset's identity: references use it, never the path, so an asset
/// can be renamed or moved as long as its .meta goes with it.
/// </summary>
/// <example>
/// <code>
/// {
///   "version": 1,
///   "guid": "3f2a9c0e8b6d4e1f9a7c5b3d1e0f2a4c",
///   "importer": "Texture",
///   "settings": {}
/// }
/// </code>
/// </example>
public sealed class AssetMeta
{
    public const int CurrentVersion = 1;
    public const string Extension = ".meta";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n", // same bytes on every OS, so .meta files don't churn in git
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public int Version { get; init; } = CurrentVersion;

    public Guid Guid { get; set; }

    /// <summary>Name of the importer that will process the asset (see <see cref="AssetImporters"/>).</summary>
    public string Importer { get; set; } = AssetImporters.Default;

    /// <summary>Importer-specific settings, kept as raw JSON so unknown keys survive a rewrite.</summary>
    public JsonObject Settings { get; set; } = new();

    public static AssetMeta Create(string importer) => new() { Guid = Guid.NewGuid(), Importer = importer };

    /// <summary>Reads a .meta file. Returns null when it is not valid JSON or has no valid GUID.</summary>
    public static AssetMeta? Load(string path)
    {
        try
        {
            MetaFile? file = JsonSerializer.Deserialize<MetaFile>(File.ReadAllText(path), JsonOptions);
            if (file == null || !Guid.TryParse(file.Guid, out Guid guid) || guid == Guid.Empty)
                return null;
            return new AssetMeta
            {
                Version = file.Version,
                Guid = guid,
                Importer = string.IsNullOrWhiteSpace(file.Importer) ? AssetImporters.Default : file.Importer,
                Settings = file.Settings ?? new JsonObject(),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(string path)
    {
        var file = new MetaFile
        {
            Version = Version,
            Guid = Guid.ToString("N"),
            Importer = Importer,
            Settings = Settings,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOptions) + "\n");
    }

    /// <summary>On-disk shape: the GUID is written as 32 hex digits, without dashes.</summary>
    private sealed class MetaFile
    {
        public int Version { get; set; }
        public string? Guid { get; set; }
        public string? Importer { get; set; }
        public JsonObject? Settings { get; set; }
    }
}

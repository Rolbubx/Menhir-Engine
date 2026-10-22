using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Menhir.Editor.Updates;

/// <summary>A release of Menhir on GitHub, with the package for this platform.</summary>
public sealed record Release(Version Version, string Tag, string Name, string Notes, string PackageUrl, long PackageSize);

/// <summary>
/// Looks for a newer Menhir among the GitHub releases of the repository (made by .github/workflows/release.yml:
/// one per tag vX.Y.Z, with a Menhir-Setup-&lt;platform&gt;.zip per platform), and downloads it.
/// </summary>
public sealed class UpdateChecker(HttpClient http)
{
    public const string Repository = "Rolbubx/Menhir-Engine";

    /// <summary>The running editor's version (Directory.Build.props, or the release's tag).</summary>
    public static Version CurrentVersion
    {
        get
        {
            Version v = Assembly.GetEntryAssembly()?.GetName().Version ?? typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>The package's name for this platform, e.g. Menhir-Setup-win-x64.zip.</summary>
    public static string PackageName => $"Menhir-Setup-{RuntimeInformation.RuntimeIdentifier}.zip";

    public static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MenhirEditor", CurrentVersion.ToString()));
        return client;
    }

    /// <summary>The latest release, or null when there is none (or none with a package for this platform).</summary>
    /// <exception cref="HttpRequestException">GitHub cannot be reached, or refuses (e.g. a private repository).</exception>
    public async Task<Release?> LatestAsync(CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using HttpResponseMessage response = await http.SendAsync(request, cancel);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null; // no release yet (or a private repository)
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancel), PackageName);
    }

    /// <summary>A release from GitHub's JSON, with the asset named <paramref name="package"/>; null without it.</summary>
    public static Release? Parse(string json, string package)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("draft", out JsonElement draft) && draft.GetBoolean() ||
            root.TryGetProperty("prerelease", out JsonElement pre) && pre.GetBoolean())
            return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (ParseVersion(tag) is not { } version)
            return null;
        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != package)
                continue;
            return new Release(version, tag, root.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? tag : tag,
                root.TryGetProperty("body", out JsonElement body) ? body.GetString() ?? "" : "",
                asset.GetProperty("browser_download_url").GetString()!, asset.GetProperty("size").GetInt64());
        }
        return null;
    }

    /// <summary>"v1.2.3" or "1.2.3" as a version (missing parts are 0), or null.</summary>
    public static Version? ParseVersion(string tag)
    {
        string text = tag.Trim().TrimStart('v', 'V');
        int end = text.IndexOfAny(['-', '+']);
        if (end >= 0)
            text = text[..end];
        if (!Version.TryParse(text.Contains('.') ? text : text + ".0", out Version? v))
            return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    /// <summary>Downloads the release's package to <paramref name="path"/>, reporting the fraction done (0 to 1).</summary>
    public async Task DownloadAsync(Release release, string path, IProgress<double>? progress, CancellationToken cancel = default)
    {
        using HttpResponseMessage response = await http.GetAsync(release.PackageUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? release.PackageSize;
        string partial = path + ".part";
        await using (Stream source = await response.Content.ReadAsStreamAsync(cancel))
        await using (FileStream target = File.Create(partial))
        {
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;
                if (total > 0)
                    progress?.Report(Math.Min(1.0, done / (double)total));
            }
        }
        File.Move(partial, path, overwrite: true);
    }
}

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PolarUninstallerWpf.Services;

public enum UpdateState { Checking, Available, UpToDate, NoCompatibleRelease, Failed }
public sealed record WpfAsset(string Name, string Url, long Size, string Digest);
public sealed record WpfRelease(string Version, string Title, string Notes, DateTimeOffset? Published, string Url, IReadOnlyList<string> Assets)
{
    public IReadOnlyList<WpfAsset> Downloads { get; init; } = [];
}
public sealed record UpdateResult(UpdateState State, WpfRelease? Release = null, string? Error = null);

public sealed class WpfUpdateService
{
    public const string StandaloneAssetName = "PolarUninstaller.exe";
    public const string DefaultApiBase = "https://api.github.com/repos/Ayh-m1/Polar-Uninstaller";

    public static string CurrentVersion => AppInfo.Version;
    public static void Log(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "PolarUninstaller-Update.log");
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private readonly HttpClient client;
    private readonly string apiBase;
    private readonly string currentVersion;
    private UpdateResult? cached;
    private DateTimeOffset cachedAt;
    private Task<UpdateResult>? running;
    private readonly object gate = new();

    public WpfUpdateService() : this(CreateClient(), DefaultApiBase, AppInfo.Version) { }

    /// <summary>Test seam: allows a deterministic HTTP handler and a simulated current version.</summary>
    internal WpfUpdateService(HttpClient client, string apiBase, string currentVersion)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.apiBase = apiBase.TrimEnd('/');
        this.currentVersion = currentVersion;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"PolarUninstallerWPF/{AppInfo.Version}");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.ExpectContinue = false;
        return client;
    }

    /// <summary>
    /// A generic release is compatible when it contains either the versioned Setup
    /// package or the standalone EXE. Older Python releases used
    /// "Polar.Uninstaller.exe" (with a dot) and therefore never match the standalone
    /// asset name. Tags prefixed with wpf-v remain compatible by convention.
    /// </summary>
    internal static bool IsCompatibleRelease(string tag, string versionText, IReadOnlyList<string> assets)
    {
        if (tag.StartsWith("wpf-v", StringComparison.OrdinalIgnoreCase)) return true;
        string setup = $"PolarUninstaller-Setup-v{versionText}.exe";
        return assets.Any(a => string.Equals(a, setup, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(a, StandaloneAssetName, StringComparison.OrdinalIgnoreCase));
    }

    public Task<UpdateResult> CheckAsync(bool force = false)
    {
        lock (gate)
        {
            if (running is { IsCompleted: false }) return running;
            if (!force && cached != null && DateTimeOffset.UtcNow - cachedAt < TimeSpan.FromMinutes(5)) return Task.FromResult(cached);
            return running = FetchAsync();
        }
    }

    private async Task<UpdateResult> FetchAsync()
    {
        Log($"FetchAsync start; current={currentVersion}");
        try
        {
            var matches = new List<(SemanticVersion Version, WpfRelease Release)>();
            for (int page = 1; page <= 5; page++)
            {
                using var response = await client.GetAsync($"{apiBase}/releases?per_page=100&page={page}");
                Log($"page {page}: status={(int)response.StatusCode} {response.StatusCode}");
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) { Log("rate limited"); return new(UpdateState.Failed, Error: "rate_or_access"); }
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var json = await JsonDocument.ParseAsync(stream);
                if (json.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException();
                int count = 0;
                foreach (var item in json.RootElement.EnumerateArray())
                {
                    count++;
                    if (item.ValueKind != JsonValueKind.Object) throw new JsonException();
                    if (item.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
                    string tag = Text(item, "tag_name");
                    if (item.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) continue;
                    string versionText = tag.StartsWith("wpf-v", StringComparison.OrdinalIgnoreCase) ? tag[5..] : tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;
                    if (!SemanticVersion.TryParse(versionText, out var version)) continue;
                    string url = Text(item, "html_url");
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) || parsedUrl.Scheme != Uri.UriSchemeHttps || !string.Equals(parsedUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase) || !parsedUrl.AbsolutePath.StartsWith("/Ayh-m1/Polar-Uninstaller/releases/tag/", StringComparison.OrdinalIgnoreCase)) continue;
                    var assets = new List<string>();
                    var downloads = new List<WpfAsset>();
                    if (item.TryGetProperty("assets", out var assetList) && assetList.ValueKind == JsonValueKind.Array)
                        foreach (var asset in assetList.EnumerateArray()) if (asset.ValueKind == JsonValueKind.Object)
                        {
                            string assetName = Text(asset, "name");
                            if (assetName.Length == 0) continue;
                            assets.Add(assetName);
                            long size = asset.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var bytes) ? bytes : 0;
                            downloads.Add(new(assetName, Text(asset, "browser_download_url"), size, Text(asset, "digest")));
                        }
                    if (!IsCompatibleRelease(tag, versionText, assets)) continue;
                    DateTimeOffset? published = DateTimeOffset.TryParse(Text(item, "published_at"), out var date) ? date : null;
                    matches.Add((version!, new WpfRelease(versionText, Text(item, "name"), Text(item, "body"), published, url, assets) { Downloads = downloads }));
                }
                if (count < 100) break;
                if (page == 5) return new(UpdateState.Failed, Error: "too_many_pages");
            }
            var latest = matches.OrderByDescending(x => x.Version).FirstOrDefault();
            Log($"matches={matches.Count} latest={latest.Release?.Version}");
            UpdateResult result = latest.Release == null ? new(UpdateState.NoCompatibleRelease) :
                new(latest.Version.CompareTo(SemanticVersion.Parse(currentVersion)) > 0 ? UpdateState.Available : UpdateState.UpToDate, latest.Release);
            lock (gate) { cached = result; cachedAt = DateTimeOffset.UtcNow; }
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or System.IO.IOException)
        { Log($"FetchAsync exception: {ex.GetType().FullName}: {ex.Message}; inner={ex.InnerException?.GetType().FullName}: {ex.InnerException?.Message}"); return new(UpdateState.Failed, Error: ex is TaskCanceledException ? "timeout" : ex is JsonException ? "invalid_response" : "network"); }
    }

    /// <summary>
    /// Fetches one published release by version and returns the requested asset.
    /// Used by the external replacement helper to verify that a staged file is the
    /// exact binary GitHub publishes for that version, independently of the
    /// checksum supplied on the helper command line.
    /// </summary>
    public WpfAsset? GetPublishedAsset(string version, string assetName)
    {
        foreach (var tag in Tags(version))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{apiBase}/releases/tags/{tag}");
            using var response = client.Send(request, HttpCompletionOption.ResponseContentRead);
            if (response.StatusCode == HttpStatusCode.NotFound) continue;
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) throw new HttpRequestException("rate_or_access");
            response.EnsureSuccessStatusCode();
            using var stream = response.Content.ReadAsStream();
            using var json = JsonDocument.Parse(stream);
            var asset = ParsePublishedAsset(json.RootElement, assetName);
            if (asset != null) return asset;
        }
        return null;
    }

    public async Task<WpfAsset?> GetPublishedAssetAsync(string version, string assetName, CancellationToken token = default)
    {
        foreach (var tag in Tags(version))
        {
            using var response = await client.GetAsync($"{apiBase}/releases/tags/{tag}", token);
            if (response.StatusCode == HttpStatusCode.NotFound) continue;
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) throw new HttpRequestException("rate_or_access");
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var asset = ParsePublishedAsset(json.RootElement, assetName);
            if (asset != null) return asset;
        }
        return null;
    }

    private static WpfAsset? ParsePublishedAsset(JsonElement root, string assetName)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object) continue;
            string name = Text(asset, "name");
            if (!string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase)) continue;
            long size = asset.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var bytes) ? bytes : 0;
            return new WpfAsset(name, Text(asset, "browser_download_url"), size, Text(asset, "digest"));
        }
        return null;
    }

    private static IEnumerable<string> Tags(string version)
    {
        yield return $"v{version}";
        yield return $"wpf-v{version}";
    }

    private static string Text(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}

internal sealed class SemanticVersion : IComparable<SemanticVersion>
{
    private static readonly Regex Pattern = new(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant);
    private readonly long major, minor, patch;
    private readonly string[] pre;
    private SemanticVersion(long major, long minor, long patch, string[] pre) { this.major = major; this.minor = minor; this.patch = patch; this.pre = pre; }
    public static SemanticVersion Parse(string text) => TryParse(text, out var v) ? v! : throw new FormatException();
    public static bool TryParse(string text, out SemanticVersion? version)
    {
        version = null; var match = Pattern.Match(text);
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out var a) || !long.TryParse(match.Groups[2].Value, out var b) || !long.TryParse(match.Groups[3].Value, out var c)) return false;
        var pre = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : Array.Empty<string>();
        if (pre.Any(x => x.Length > 1 && x[0] == '0' && x.All(char.IsAsciiDigit))) return false;
        version = new(a, b, c, pre); return true;
    }
    public int CompareTo(SemanticVersion? other)
    {
        if (other == null) return 1;
        int n = major.CompareTo(other.major); if (n != 0) return n; n = minor.CompareTo(other.minor); if (n != 0) return n; n = patch.CompareTo(other.patch); if (n != 0) return n;
        if (pre.Length == 0 || other.pre.Length == 0) return other.pre.Length.CompareTo(pre.Length);
        for (int i = 0; i < Math.Min(pre.Length, other.pre.Length); i++)
        {
            string a = pre[i], b = other.pre[i]; bool na = a.All(char.IsAsciiDigit), nb = b.All(char.IsAsciiDigit);
            n = na && nb ? (a.Length == b.Length ? string.CompareOrdinal(a, b) : a.Length.CompareTo(b.Length)) : na != nb ? (na ? -1 : 1) : string.CompareOrdinal(a, b);
            if (n != 0) return n;
        }
        return pre.Length.CompareTo(other.pre.Length);
    }
}

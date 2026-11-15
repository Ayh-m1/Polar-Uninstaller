using System.IO;
using System.Text.Json;

namespace PolarUninstallerWpf.Services;

/// <summary>
/// Steam metadata helpers. Local manifests remain the authoritative source for
/// installed game identity; this service only adds a SteamDB reference URL and
/// an optional on-disk cache for any extra public metadata that may be fetched.
/// It never blocks offline detection.
/// </summary>
public static class SteamMetadataService
{
    public static string? SteamDbUrl(InstalledProgram program)
    {
        if (!program.Identifier.StartsWith("steam:", StringComparison.OrdinalIgnoreCase)) return null;
        string appId = program.Identifier["steam:".Length..];
        if (appId.Length == 0 || !appId.All(char.IsAsciiDigit) || appId == "0") return null;
        return $"https://steamdb.info/app/{appId}/";
    }

    public static string? SteamDbUrl(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || !appId.All(char.IsAsciiDigit) || appId == "0") return null;
        return $"https://steamdb.info/app/{appId}/";
    }

    private static string CachePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PolarUninstallerWPF-Development", "steam-metadata-cache.json");

    public static Dictionary<string, string> LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return new Dictionary<string, string>(StringComparer.Ordinal);
            using var document = JsonDocument.Parse(File.ReadAllText(CachePath));
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var property in document.RootElement.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.String)
                    result[property.Name] = property.Value.GetString() ?? "";
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    public static void SaveCache(IReadOnlyDictionary<string, string> cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(cache));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }
}

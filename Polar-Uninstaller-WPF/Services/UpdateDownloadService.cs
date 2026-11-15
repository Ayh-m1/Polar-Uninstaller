using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Diagnostics;

namespace PolarUninstallerWpf.Services;
public sealed record DownloadProgress(long Bytes, long? Total, double BytesPerSecond);
public static class UpdateDownloadService
{
    private static readonly object ActiveLock = new();
    private static readonly HashSet<string> ActiveDirectories = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterActiveDirectory(string directory)
    {
        string full = Normalize(directory);
        lock (ActiveLock) ActiveDirectories.Add(full);
    }

    public static void UnregisterActiveDirectory(string directory)
    {
        string full = Normalize(directory);
        lock (ActiveLock) ActiveDirectories.Remove(full);
    }

    public static bool IsActiveDirectory(string path)
    {
        string full;
        try { full = Normalize(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        string prefix = full + Path.DirectorySeparatorChar;
        lock (ActiveLock)
            return ActiveDirectories.Any(active => string.Equals(full, active, StringComparison.OrdinalIgnoreCase) || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Removes stale staging folders left by an interrupted or cancelled update.
    /// Only folders created by this application are considered, and only when
    /// they are at least a day old, so an update in progress is never touched.
    /// </summary>
    public static void CleanupStaleDownloads()
    {
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(Path.GetTempPath(), "PolarWpfUpdate-*"))
            {
                if (IsActiveDirectory(directory)) continue;
                try
                {
                    if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-1)) Directory.Delete(directory, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static WpfAsset? Select(WpfRelease release) =>
        release.Downloads.FirstOrDefault(a => string.Equals(a.Name, WpfUpdateService.StandaloneAssetName, StringComparison.OrdinalIgnoreCase))
        ?? release.Downloads.FirstOrDefault(a => string.Equals(a.Name, $"PolarUninstaller-Setup-v{release.Version}.exe", StringComparison.OrdinalIgnoreCase));

    public static Task<string> DownloadAsync(WpfRelease release, IProgress<DownloadProgress> progress, CancellationToken token) =>
        DownloadAsync(release, progress, token, WpfUpdateService.CurrentVersion);

    internal static async Task<string> DownloadAsync(WpfRelease release, IProgress<DownloadProgress> progress, CancellationToken token, string currentVersion, string? stagingRoot = null, string? downloadBase = null)
    {
        if (SemanticVersion.Parse(release.Version).CompareTo(SemanticVersion.Parse(currentVersion)) <= 0) throw new InvalidDataException("older_version");
        var asset = Select(release) ?? throw new InvalidDataException("no_installer");
        if (!asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || asset.Digest.Length != 71 || !asset.Digest[7..].All(Uri.IsHexDigit)) throw new InvalidDataException("missing_checksum");
        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri)) throw new InvalidDataException("invalid_url");
        // The optional downloadBase is an internal test seam used by the
        // regression suite with a local fixture server. Production calls leave
        // it null and therefore keep the strict GitHub-only URL check.
        bool validUrl = string.IsNullOrWhiteSpace(downloadBase)
            ? uri.Scheme == Uri.UriSchemeHttps &&
              uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
              uri.AbsolutePath.StartsWith("/Ayh-m1/Polar-Uninstaller/releases/download/", StringComparison.OrdinalIgnoreCase)
            : asset.Url.StartsWith(downloadBase.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
        if (!validUrl) throw new InvalidDataException("invalid_url");
        string root = string.IsNullOrWhiteSpace(stagingRoot) ? Path.GetTempPath() : stagingRoot;
        bool enoughSpace = true;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(root)!);
            long required = (asset.Size > 0 ? asset.Size : 0) + 16L * 1024 * 1024;
            if (drive.AvailableFreeSpace > 0 && drive.AvailableFreeSpace < required) enoughSpace = false;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }
        if (!enoughSpace) throw new InvalidDataException("insufficient_space");
        string directory = Path.Combine(root, "PolarWpfUpdate-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, asset.Name);
        RegisterActiveDirectory(directory);
        try
        {
            const int maxAttempts = 3;
            Exception? lastError = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) }; client.DefaultRequestHeaders.UserAgent.ParseAdd($"PolarUninstallerWPF/{AppInfo.Version}");
                    using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
                    long? total = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : null);
                    await using var input = await response.Content.ReadAsStreamAsync(token);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long received = 0, previous = 0; double speed = 0; var clock = Stopwatch.StartNew(); double previousTime = 0;
                    const int bufferSize = 1024 * 1024;
                    await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, true))
                    {
                        byte[] buffer = new byte[bufferSize]; int read;
                        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, read), token); hash.AppendData(buffer, 0, read); received += read;
                            double now = clock.Elapsed.TotalSeconds;
                            if (now - previousTime >= .25)
                            {
                                double measured = (received - previous) / Math.Max(now - previousTime, 0.001); speed = speed == 0 ? measured : speed * .7 + measured * .3;
                                progress.Report(new(received, total, speed)); previous = received; previousTime = now;
                            }
                        }
                    }
                    if (asset.Size > 0 && received != asset.Size || total.HasValue && received != total.Value) throw new InvalidDataException("size_mismatch");
                    if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(asset.Digest[7..]))) throw new InvalidDataException("checksum_mismatch");
                    if (string.Equals(asset.Name, WpfUpdateService.StandaloneAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        var verification = UpdateVerificationService.VerifyStandaloneExecutable(target, release.Version, asset.Digest[7..], received);
                        if (!verification.IsValid) { try { File.Delete(target); } catch { } throw new InvalidDataException(verification.Error ?? "invalid_executable"); }
                        if (verification.Signature == AuthenticodeStatus.Unsigned) WpfUpdateService.Log("Downloaded update is unsigned; the published GitHub digest is the remaining authenticity check.");
                    }
                    progress.Report(new(received, received, speed)); return target;
                }
                catch (OperationCanceledException) { if (File.Exists(target)) File.Delete(target); throw; }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
                {
                    lastError = ex; if (File.Exists(target)) File.Delete(target);
                    if (attempt < maxAttempts) { await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), token); continue; }
                }
                catch (InvalidDataException) { if (File.Exists(target)) File.Delete(target); throw; }
            }
            if (Directory.Exists(directory)) try { Directory.Delete(directory); } catch { }
            throw lastError ?? new IOException("download_failed");
        }
        finally { UnregisterActiveDirectory(directory); }
    }
}

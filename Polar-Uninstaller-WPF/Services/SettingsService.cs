using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PolarUninstallerWpf.Services;

public sealed record AppSettings(
    string Theme = ThemeCatalog.DefaultTheme,
    bool Arabic = false,
    bool SidebarCollapsed = false,
    string[]? CleanerCategories = null,
    string? DeepUninstallerSort = null,
    string? FileBrowserSort = null,
    string? FileBrowserSortDirection = null,
    string? DuplicateSort = null,
    bool DuplicateSortAscending = false,
    bool SelectionTipDismissed = false,
    bool ShowHiddenItems = false);

public sealed class SettingsService
{
    public string? LoadError { get; private set; }
    private readonly string[] paths;
    private string? activePath;
    private volatile AppSettings? memory;private readonly object persistence=new();private long generation;private Task lastSave=Task.CompletedTask;

    public SettingsService(string? isolatedDirectory = null)
    {
        paths = isolatedDirectory is not null
            ? new[] { Path.Combine(isolatedDirectory, "settings.json") }
            : new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData }
                .Select(folder => Environment.GetFolderPath(folder))
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Select(folder => Path.Combine(folder, "PolarUninstallerWPF-Development", "settings.json"))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public AppSettings Load()
    {
        if(memory is {} cached)return cached;
        foreach (var candidate in paths.SelectMany(p=>new[]{p,p+".bak"}))
        {
            try
            {
                if(new FileInfo(candidate).Length>1024*1024)throw new InvalidDataException("Settings file exceeds the supported size.");
                string raw=File.ReadAllText(candidate);
                var settings=JsonSerializer.Deserialize<AppSettings>(raw);
                if(settings is null)continue;
                activePath=candidate.EndsWith(".bak",StringComparison.OrdinalIgnoreCase)?candidate[..^4]:candidate;LoadError=candidate.EndsWith(".bak",StringComparison.OrdinalIgnoreCase)?"Recovered settings from backup.":null;
                var normalized=settings with{Theme=ThemeCatalog.Resolve(settings.Theme)};
                if(ThemeCatalog.IsLegacyDefault(settings.Theme))
                {
                    try {MigrateThemeName(candidate,raw,normalized.Theme);}
                    catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException){LoadError="Theme preference migration could not be saved: "+error.Message;}
                }
                memory=normalized;return normalized;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException) { LoadError = error.Message; }
        }
        return memory=new();
    }

    private static void MigrateThemeName(string path,string captured,string theme)
    {
        // Change only the theme field, including when a newer settings writer
        // has added properties unknown to this version of AppSettings.
        var document=JsonNode.Parse(captured)!.AsObject();document[nameof(AppSettings.Theme)]=theme;
        if(File.ReadAllText(path)!=captured)return; // Never overwrite a newer save.
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temporary,document.ToJsonString());File.Move(temporary,path,true);}
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }

    public void Save(AppSettings settings){memory=settings with{Theme=ThemeCatalog.Resolve(settings.Theme)};Interlocked.Increment(ref generation);lock(persistence)Persist(memory);}
    public Task SaveAsync(AppSettings settings)
    {
        var captured=memory=settings with{Theme=ThemeCatalog.Resolve(settings.Theme)};long version=Interlocked.Increment(ref generation);
        return lastSave=Task.Run(()=>{lock(persistence){if(version==Interlocked.Read(ref generation))Persist(captured);}});
    }
    public Task FlushAsync()=>lastSave;
    private void Persist(AppSettings settings)
    {
        settings=settings with{Theme=ThemeCatalog.Resolve(settings.Theme)};
        Exception? lastError = null;
        var candidates = activePath is null ? paths : new[] { activePath }.Concat(paths.Where(p => p != activePath));
        foreach (var candidate in candidates)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(candidate)!);
                AtomicStateFile.Write(candidate,JsonSerializer.Serialize(settings),LoadError==null);
                activePath = candidate;
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { lastError = error; }
        }
        throw lastError ?? new IOException("No per-user settings directory is available.");
    }
}

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;

namespace PolarUninstallerWpf.Services;

public sealed record EpicRemovalPlan(InstalledProgram Game,string Root,string AppName,string CatalogId,string Namespace,
    string LaunchPath,string ItemPath,byte[] ItemBytes,string BuildPath,byte[] BuildBytes,
    IReadOnlyList<EpicInstalledFile> Files,IReadOnlyDictionary<string,byte[]> Indexes,bool RecordsOnly=false,IReadOnlyList<EpicEoshRecord>? Eosh=null);
public sealed record EpicRemovalResult(bool Confirmed,int Removed,int Preserved,string Backup,IReadOnlyList<string> Messages);

/// <summary>Direct removal of exactly manifest-listed Epic payloads.
/// No launcher protocols, recursive directory deletion, or shared Registry edits.
/// Unlisted/modified files and save data are retained. Metadata is committed only
/// after all eligible payloads are absent, with exact-byte backups and rechecks.</summary>
public sealed class EpicGameRemovalService
{
    private const long LargeFileBytes = 16L * 1024 * 1024;
    private const int MaxVerifiedFileHandles = 256;

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FileIdentity(ulong Volume, ulong IdLow, ulong IdHigh);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle, int infoClass, out FileIdentity identity, uint size);

    private static bool TryGetFileIdentity(FileStream stream, out FileIdentity identity) =>
        GetFileInformationByHandleEx(stream.SafeFileHandle, 18 /* FileIdInfo */, out identity, 24) &&
        (identity.IdLow != 0 || identity.IdHigh != 0);

    public static bool Enabled=>true;
    private readonly Func<IEnumerable<string>> manifests;
    private readonly Func<IEnumerable<string>> indexes;
    private readonly Func<bool> launcherRunning;
    private readonly string backupRoot;
    private readonly IEnumerable<string>? eoshRoots;
    public EpicGameRemovalService():this(LauncherMetadataDiscovery.EpicManifestFiles,LauncherMetadataDiscovery.EpicIndexFiles,
        EpicLauncherCloseService.AnyRunning,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PolarUninstallerWPF-Development","GameRemovalBackups","Epic")){ }
    internal EpicGameRemovalService(Func<IEnumerable<string>> manifests,Func<IEnumerable<string>> indexes,Func<bool> running,string backupRoot,IEnumerable<string>? eoshRoots=null)
    {this.manifests=manifests;this.indexes=indexes;launcherRunning=running;this.backupRoot=backupRoot;this.eoshRoots=eoshRoots;}
    private static string Text(JsonElement root,string key)=>LauncherMetadataDiscovery.Text(root,key);
    private static bool Same(string a,string b)=>LeftoverService.EqualPath(a,b);
    private static bool Inside(string path,string root)=>Path.GetFullPath(path).StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
    private static byte[] Bytes(string path)
    {
        var info=new FileInfo(path);if(info.Length>128*1024*1024)throw new IOException("Epic metadata exceeds the supported size.");
        return File.ReadAllBytes(path);
    }
    private static void Physical(string path,string root)
    {
        if(!Inside(path,root))throw new IOException("Epic file is outside its verified installation.");
        for(string? p=path;p!=null;p=Path.GetDirectoryName(p))
        {
            try{if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Epic removal through links or junctions is blocked.");}
            catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
        }
    }
    private static bool Personal(string relative)=>relative.Split('\\','/').Any(x=>new[]{"saves","save","saved","savedgames","saved games","userdata","user data","profiles","screenshots","backups"}.Contains(x,StringComparer.OrdinalIgnoreCase))
        ||new[]{".sav",".save",".sqlite",".sqlite3",".db",".ini",".cfg",".config"}.Contains(Path.GetExtension(relative),StringComparer.OrdinalIgnoreCase);

    internal static bool MatchesBuild(EpicBuildFiles build,JsonElement item,IReadOnlyList<EpicEoshRecord> eosh)
    {
        string root=Text(item,"InstallLocation"),launch=Text(item,"LaunchExecutable"),version=Text(item,"AppVersionString");
        if(!Same(Path.Combine(root,build.LaunchExecutable),Path.Combine(root,launch)))return false;
        if(version.Length>0&&version!=build.BuildVersion)return false;
        if(build.AppName==Text(item,"AppName"))return true;
        // Publisher build names are not necessarily store artifact identifiers.
        // Accept an alias only when the exact, identity-validated EOSH record
        // independently agrees on both build version and launch executable.
        if(eosh.Count!=1||version.Length==0)return false;
        using var doc=JsonDocument.Parse(eosh[0].Bytes);
        if(!doc.RootElement.TryGetProperty("v4",out var entry)||!entry.TryGetProperty("manifestData",out var data)||data.ValueKind!=JsonValueKind.Object)return false;
        return Text(data,"version")==version&&Same(Path.Combine(root,Text(data,"launchExe")),Path.Combine(root,launch));
    }

    private byte[]? RecoveryBuild(string itemPath,JsonElement item)
    {
        if(!Directory.Exists(backupRoot))return null;
        foreach(var folder in new DirectoryInfo(backupRoot).EnumerateDirectories().OrderByDescending(d=>d.CreationTimeUtc).Take(256))
        {
            try
            {
                using var identity=JsonDocument.Parse(Bytes(Path.Combine(folder.FullName,"Identity.json")));
                using var previous=JsonDocument.Parse(Bytes(Path.Combine(folder.FullName,"Epic.item")));
                var old=previous.RootElement;var id=identity.RootElement;
                if(!Same(Text(id,"Root"),Text(item,"InstallLocation"))||!Same(Text(id,"ItemPath"),itemPath))continue;
                if(new[]{"AppName","CatalogItemId","CatalogNamespace","InstallationGuid","LaunchExecutable"}.Any(k=>!Text(old,k).Equals(Text(item,k),StringComparison.Ordinal)))continue;
                if(!Same(Text(old,"InstallLocation"),Text(item,"InstallLocation")))continue;
                byte[] build=Bytes(Path.Combine(folder.FullName,"Build.manifest"));
                string eosh=Path.Combine(folder.FullName,"EOSH-game.egi");
                var records=File.Exists(eosh)?new[]{new EpicEoshRecord(eosh,Bytes(eosh))}:Array.Empty<EpicEoshRecord>();
                if(MatchesBuild(EpicBuildManifestReader.Read(build),old,records))return build;
            }
            catch(Exception error)when(LauncherMetadataDiscovery.ReadFailure(error)){}
        }
        return null;
    }
    public IReadOnlyList<InstalledProgram> RecoverableGames()
    {
        var games=new List<InstalledProgram>();if(!Enabled)return games;
        foreach(string path in manifests())
        {
            try
            {
                using var doc=JsonDocument.Parse(Bytes(path));var item=doc.RootElement;string location=Text(item,"InstallLocation");
                if(!Path.IsPathFullyQualified(location)||Directory.Exists(location)||RecoveryBuild(path,item)==null)continue;
                DirectRemovalService.ValidateRoot(location);BrowserOperationsService.VerifyAbsent(location);
                games.Add(new(Text(item,"DisplayName"),"Epic Games",Text(item,"AppVersionString"),location,Source:"Epic Games",Identifier:"epic:"+Text(item,"CatalogItemId"),RequiresOfficialRemoval:true));
            }
            catch(Exception error)when(LauncherMetadataDiscovery.ReadFailure(error)){}
        }
        return games;
    }

    public Task<EpicRemovalPlan> PrepareAsync(InstalledProgram game,IReadOnlyList<InstalledProgram> inventory,CancellationToken token)=>Task.Run(()=>
    {
        if(!game.Identifier.StartsWith("epic:",StringComparison.OrdinalIgnoreCase)||!Path.IsPathFullyQualified(game.Location))throw new IOException("Verified Epic identity is required.");
        string root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(game.Location));DirectRemovalService.ValidateRoot(root);
        bool recordsOnly=!Directory.Exists(root);
        if(root.StartsWith("\\\\",StringComparison.Ordinal))throw new IOException("A local installed Epic game is required.");
        if(Inside(Environment.ProcessPath??AppContext.BaseDirectory,root)||Inside(AppContext.BaseDirectory,root))throw new IOException("Polar cannot remove itself.");
        foreach(var other in inventory)
        {
            if(UninstallVerificationService.Matches(game,other))continue;
            if(Path.IsPathFullyQualified(other.Location)&&(Same(root,other.Location)||Inside(other.Location,root)||Inside(root,other.Location)))
                throw new IOException("Another installation overlaps this game directory.");
        }
        var matches=new List<(string Path,byte[] Bytes)>();
        foreach(string file in manifests().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();var bytes=Bytes(file);using var json=JsonDocument.Parse(bytes);var item=json.RootElement;
            string location=Text(item,"InstallLocation");
            if(!Path.IsPathFullyQualified(location))continue;
            if(!Same(root,location))
            {if(Inside(location,root)||Inside(root,location))throw new IOException("Another Epic installation shares this directory tree.");continue;}
            matches.Add((file,bytes));
        }
        if(matches.Count!=1)throw new IOException("Exactly one matching Epic item manifest is required.");
        var match=matches[0];using var doc=JsonDocument.Parse(match.Bytes);var entry=doc.RootElement;
        string app=Text(entry,"AppName"),catalog=Text(entry,"CatalogItemId"),ns=Text(entry,"CatalogNamespace"),guid=Text(entry,"InstallationGuid");
        // Epic installation IDs are opaque hex filename tokens. Real launcher
        // records can omit leading zeroes; parsing them as a .NET GUID rejects
        // valid installations. Keep the exact token and bind it to the captured
        // .item, local build manifest and EOSH record instead of normalizing it.
        if(app.Length==0||catalog.Length==0||ns.Length==0||guid.Length is <1 or >32||!guid.All(char.IsAsciiHexDigit)||
            !(game.Identifier.Equals("epic:"+catalog,StringComparison.OrdinalIgnoreCase)||game.Identifier.Equals("epic:"+app,StringComparison.OrdinalIgnoreCase))||
            Text(entry,"MainGameAppName").Length>0||Text(entry,"HostInstallationGuid").Length>0||
            (entry.TryGetProperty("bIsIncompleteInstall",out var partial)&&partial.ValueKind==JsonValueKind.True))
            throw new IOException("The Epic item is incomplete, shared, or its game identity changed.");
        string metadata=Path.Combine(root,".egstore"),build=Path.Combine(metadata,guid+".manifest");Physical(build,root);Physical(match.Path,Path.GetDirectoryName(Path.GetFullPath(match.Path))!);
        byte[] buildBytes=recordsOnly?RecoveryBuild(match.Path,entry)??throw new IOException("No authorized Epic removal backup matches this stale record."):Bytes(build);
        if(recordsOnly)BrowserOperationsService.VerifyAbsent(root);
        var installed=EpicBuildManifestReader.Read(buildBytes);
        var eosh=EpicEoshStore.Capture(app,ns,catalog,root,guid,eoshRoots);
        string launch=Path.GetFullPath(Path.Combine(root,Text(entry,"LaunchExecutable")));Physical(launch,root);
        if(!MatchesBuild(installed,entry,eosh)||
            !installed.Files.Any(f=>Same(Path.Combine(root,f.RelativePath),launch)))throw new IOException("Epic build and item identities do not agree.");
        foreach(var file in installed.Files){token.ThrowIfCancellationRequested();Physical(Path.Combine(root,file.RelativePath),root);}
        var captures=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach(var index in indexes().Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
        {
            var bytes=Bytes(index);using var json=JsonDocument.Parse(bytes);
            if(!json.RootElement.TryGetProperty("InstallationList",out var list)||list.ValueKind!=JsonValueKind.Array)throw new IOException("Epic index format is unsupported.");
            foreach(var item in list.EnumerateArray().Where(x=>Same(Text(x,"InstallLocation"),root)))
                if(!Text(item,"AppName").Equals(app,StringComparison.Ordinal))throw new IOException("Conflicting Epic installation-index identity.");
            captures[index]=bytes;
        }
        return new EpicRemovalPlan(game,root,app,catalog,ns,launch,match.Path,match.Bytes,build,buildBytes,installed.Files,captures,recordsOnly,eosh);
    },token);

    public Task<EpicRemovalResult> RemoveAsync(EpicRemovalPlan plan,IReadOnlyList<InstalledProgram> inventory,IProgress<string>? progress,CancellationToken token)=>Task.Run(async()=>
    {
        await using var changes=new FileChangeCollector();
        var messages=new List<string>();int removed=0,preserved=0;
        void Idle(bool full=true){if(launcherRunning())throw new IOException("Epic restarted during removal; the operation stopped safely.");if(full&&DirectRemovalService.RunningProcesses(plan.Root).Count>0)throw new IOException("Close the selected game before removing it.");}
        Idle();var fresh=await PrepareAsync(plan.Game,inventory,token);
        bool Equal(byte[] a,byte[] b)=>CryptographicOperations.FixedTimeEquals(SHA256.HashData(a),SHA256.HashData(b));
        void CheckMetadata()
        {
            Physical(plan.ItemPath,Path.GetDirectoryName(plan.ItemPath)!);Physical(plan.BuildPath,plan.Root);
            if(plan.RecordsOnly)BrowserOperationsService.VerifyAbsent(plan.Root);
            if(!Equal(Bytes(plan.ItemPath),plan.ItemBytes)||!plan.RecordsOnly&&!Equal(Bytes(plan.BuildPath),plan.BuildBytes))throw new IOException("Epic metadata changed; removal stopped.");
            foreach(var index in plan.Indexes){Physical(index.Key,Path.GetDirectoryName(index.Key)!);if(!Equal(Bytes(index.Key),index.Value))throw new IOException("Epic installation index changed; removal stopped.");}
            EpicEoshStore.Verify(plan.Eosh??Array.Empty<EpicEoshRecord>());
        }
        if(fresh.RecordsOnly!=plan.RecordsOnly||!Equal(fresh.ItemBytes,plan.ItemBytes)||!Equal(fresh.BuildBytes,plan.BuildBytes)||fresh.Indexes.Count!=plan.Indexes.Count||(fresh.Eosh?.Count??0)!=(plan.Eosh?.Count??0))throw new IOException("The captured Epic plan is stale.");
        if((fresh.Eosh??Array.Empty<EpicEoshRecord>()).Any(record=>!(plan.Eosh??Array.Empty<EpicEoshRecord>()).Any(old=>Same(old.Path,record.Path)&&Equal(old.Bytes,record.Bytes))))throw new IOException("The captured EOSH record identity changed.");
        CheckMetadata();
        var clock=Stopwatch.StartNew();var phase=Stopwatch.StartNew();
        var leases=new List<FileStream>();
        var verifiedFiles = new Dictionary<string, (FileStream Stream, FileIdentity Identity)>(StringComparer.OrdinalIgnoreCase);
        int reusedHashes = 0;
        void ReleaseLeases(){foreach(var stream in leases)stream.Dispose();leases.Clear();}
        try
        {
        foreach(var path in plan.Indexes.Keys.Prepend(plan.ItemPath).Concat(plan.RecordsOnly?Array.Empty<string>():new[]{plan.BuildPath}).Concat((plan.Eosh??Array.Empty<EpicEoshRecord>()).Select(x=>x.Path)))
            leases.Add(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read));
        CheckMetadata();
        // Preflight every installed file before deleting anything. Changed copies
        // are never authority to erase user modifications or invalidate the install.
        async Task<bool> Original(EpicInstalledFile file, bool keepVerifiedHandle)
        {
            string path=Path.Combine(plan.Root,file.RelativePath);
            Physical(path,plan.Root);
            FileStream? stream = null;
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length != file.Size) return false;
                // A live read-only share denies writers and replacement between passes.
                // Also match the newly opened path to that handle before trusting its hash.
                if (verifiedFiles.TryGetValue(path, out var verified) &&
                    TryGetFileIdentity(stream, out var currentIdentity) && currentIdentity == verified.Identity)
                {
                    reusedHashes++;
                    return true;
                }
                if (!Convert.ToHexString(await SHA1.HashDataAsync(stream, token)).Equals(file.Sha1, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (keepVerifiedHandle && TryGetFileIdentity(stream, out var identity))
                {
                    verifiedFiles.Add(path, (stream, identity));
                    stream = null;
                }
                return true;
            }
            catch(FileNotFoundException){return true;}catch(DirectoryNotFoundException){return true;}
            finally { stream?.Dispose(); }
        }
        var eligible=(plan.RecordsOnly?Array.Empty<EpicInstalledFile>():plan.Files.Where(f=>!Personal(f.RelativePath))).OrderBy(f=>Same(Path.Combine(plan.Root,f.RelativePath),plan.LaunchPath)?1:0).ToArray();
        var progressClock = Stopwatch.StartNew();
        void Report(string action, string path)
        {
            if (progressClock.ElapsedMilliseconds < 150) return;
            progress?.Report(action + path);
            progressClock.Restart();
        }
        if (eligible.Length > 0) progress?.Report("Verifying: "+eligible[0].RelativePath);
        foreach (var file in eligible)
        {
            token.ThrowIfCancellationRequested();
            Report("Verifying: ",file.RelativePath);
            bool keepHandle = file.Size >= LargeFileBytes && verifiedFiles.Count < MaxVerifiedFileHandles;
            if (!await Original(file, keepHandle)) throw new IOException("A game file was modified; it was preserved: "+file.RelativePath);
        }
        messages.Add("Epic content preflight: "+phase.ElapsedMilliseconds+" ms");phase.Restart();
        string backup=Path.Combine(backupRoot,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);
        File.WriteAllBytes(Path.Combine(backup,"Epic.item"),plan.ItemBytes);File.WriteAllBytes(Path.Combine(backup,"Build.manifest"),plan.BuildBytes);
        int number=0;foreach(var index in plan.Indexes)File.WriteAllBytes(Path.Combine(backup,"Index-"+(number++)+".json"),index.Value);
        File.WriteAllText(Path.Combine(backup,"Identity.json"),JsonSerializer.Serialize(new{plan.Root,plan.AppName,plan.CatalogId,plan.Namespace,plan.ItemPath,plan.BuildPath,Indexes=plan.Indexes.Keys}));
        var shortcuts=await ShortcutLeftoverService.ScanAsync(plan.Game,token);
        if (eligible.Length > 0) progress?.Report("Removing: "+eligible[0].RelativePath);
        foreach(var file in eligible)
        {
            token.ThrowIfCancellationRequested();Idle(false);string path=Path.Combine(plan.Root,file.RelativePath);Physical(path,plan.Root);
            if(!await Original(file, false))throw new IOException("Game file changed during removal; remaining files and metadata were preserved: "+file.RelativePath);
            if (verifiedFiles.Remove(path, out var verified)) verified.Stream.Dispose();
            try{File.GetAttributes(path);}catch(FileNotFoundException){continue;}catch(DirectoryNotFoundException){continue;}
            File.Delete(path);BrowserOperationsService.VerifyAbsent(path);changes.Add(path,file.Size);removed++;Report("Removed: ",file.RelativePath);
        }
        token.ThrowIfCancellationRequested();Idle();CheckMetadata();
        messages.Add("Epic payload removal: "+phase.ElapsedMilliseconds+" ms");ReleaseLeases();
        messages.Add("Epic large files verified without a second content read: " + reusedHashes);
        if(File.Exists(plan.LaunchPath)||eligible.Any(f=>File.Exists(Path.Combine(plan.Root,f.RelativePath))))
            return new(false,removed,preserved,backup,new[]{"Some game payloads remain. Epic installation records were retained."});
        // Commit only captured records for this exact AppName/root. Preserve all
        // other entries and unknown properties; File.Replace is same-volume atomic.
        var replaced=new List<string>();bool itemRemoved=false;
        try
        {
            EpicEoshStore.Commit(plan.Eosh??Array.Empty<EpicEoshRecord>(),backup,()=>
            {
            foreach(var index in plan.Indexes)
            {
                var json=JsonNode.Parse(index.Value)!.AsObject();var list=json["InstallationList"]!.AsArray();
                var own=list.Where(x=>x is JsonObject o&&o["AppName"]?.GetValue<string>()==plan.AppName&&Same(o["InstallLocation"]?.GetValue<string>()??"",plan.Root)).ToArray();
                if(own.Length==0)continue;
                foreach(var entry in own)list.Remove(entry);
                if(!Equal(Bytes(index.Key),index.Value))throw new IOException("Epic index changed before commit.");
                string temp=index.Key+".polar-"+Guid.NewGuid().ToString("N")+".tmp";
                try{File.WriteAllText(temp,json.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));File.Replace(temp,index.Key,null);replaced.Add(index.Key);}
                finally{if(File.Exists(temp))File.Delete(temp);}
            }
            Idle();if(!Equal(Bytes(plan.ItemPath),plan.ItemBytes))throw new IOException("Epic item changed before commit.");
            File.Delete(plan.ItemPath);BrowserOperationsService.VerifyAbsent(plan.ItemPath);itemRemoved=true;
            });
        }
        catch
        {
            // Restore only records written by this operation. Never replace a
            // concurrently changed record or delete unrelated index entries.
            foreach(var index in replaced)
            {
                try
                {
                    var json=JsonNode.Parse(File.ReadAllBytes(index))!.AsObject();var list=json["InstallationList"]!.AsArray();
                    if(!list.Any(x=>x?["AppName"]?.GetValue<string>()==plan.AppName))
                    {var original=JsonNode.Parse(plan.Indexes[index])!["InstallationList"]!.AsArray();foreach(var entry in original.Where(x=>x?["AppName"]?.GetValue<string>()==plan.AppName))list.Add(entry!.DeepClone());File.WriteAllText(index,json.ToJsonString());}
                }catch(Exception e)when(LauncherMetadataDiscovery.ReadFailure(e)){messages.Add("Epic index restoration requires review; backup: "+backup);}
            }
            if(itemRemoved&&!File.Exists(plan.ItemPath))File.WriteAllBytes(plan.ItemPath,plan.ItemBytes);
            throw;
        }
        if(!plan.RecordsOnly&&Equal(Bytes(plan.BuildPath),plan.BuildBytes)){File.Delete(plan.BuildPath);BrowserOperationsService.VerifyAbsent(plan.BuildPath);}
        foreach(var shortcut in shortcuts)messages.Add(ShortcutLeftoverService.Remove(shortcut,plan.Game));
        // Only empty parents of known installed files are removed. Unknown files,
        // modified data, game saves, and all folders outside this root are untouched.
        var directories=plan.Files.Select(f=>Path.GetDirectoryName(Path.Combine(plan.Root,f.RelativePath))!).Append(Path.GetDirectoryName(plan.BuildPath)!).Append(plan.Root)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(x=>x.Length);
        foreach(var dir in directories)try{if(Directory.Exists(dir)){if(!Same(dir,plan.Root))Physical(dir,plan.Root);Directory.Delete(dir,false);changes.Add(dir,0,true);}}catch(IOException){}catch(UnauthorizedAccessException){}
        preserved=plan.Files.Count(f=>Personal(f.RelativePath)&&File.Exists(Path.Combine(plan.Root,f.RelativePath)));
        bool registered=manifests().Where(File.Exists).Any(path=>{using var doc=JsonDocument.Parse(Bytes(path));var e=doc.RootElement;return Text(e,"AppName")==plan.AppName||Same(Text(e,"InstallLocation"),plan.Root);});
        bool indexed=plan.Indexes.Keys.Any(path=>{using var doc=JsonDocument.Parse(Bytes(path));return doc.RootElement.GetProperty("InstallationList").EnumerateArray().Any(e=>Text(e,"AppName")==plan.AppName||Same(Text(e,"InstallLocation"),plan.Root));});
        bool eoshRegistered=EpicEoshStore.IsRegistered(plan.AppName,plan.Namespace,plan.CatalogId,plan.Root,eoshRoots);
        bool confirmed=!registered&&!indexed&&!eoshRegistered&&!File.Exists(plan.LaunchPath)&&eligible.All(f=>!File.Exists(Path.Combine(plan.Root,f.RelativePath)));
        messages.Add(confirmed?"Epic payload removal and installation-record cleanup verified. Save data and unlisted files were preserved.":"Epic removal verification is incomplete.");
        messages.Add("Epic total removal: "+clock.ElapsedMilliseconds+" ms; full process scans limited to stage boundaries.");
        return new EpicRemovalResult(confirmed,removed,preserved,backup,messages);
        }
        finally
        {
            foreach (var verified in verifiedFiles.Values) verified.Stream.Dispose();
            ReleaseLeases();
        }
    },token);
}

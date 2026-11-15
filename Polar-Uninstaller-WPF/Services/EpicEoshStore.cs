using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PolarUninstallerWpf.Services;

public sealed record EpicEoshRecord(string Path,byte[] Bytes);

/// <summary>Experimental v4 EOSH per-installation records. Only the captured
/// game record is eligible. Shared services, overlays, caches and other games
/// are never removed. Changes use the store's .lock and revision counter.</summary>
public static class EpicEoshStore
{
    internal static IEnumerable<string> Roots()=>new[]{"EpicOnlineServicesShared","EpicOnlineServices"}.Select(name=>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Epic",name,"InstallHelper","InstalledItems"));
    internal static IReadOnlyList<EpicEoshRecord> Capture(string app,string ns,string catalog,string location,string guid,IEnumerable<string>? roots=null)
    {
        var records=new List<EpicEoshRecord>();
        foreach(string root in roots??Roots())
        {
            string path=Path.Combine(root,guid+".egi");
            if(!File.Exists(path))continue;
            Physical(path);byte[] bytes=Read(path);using var doc=JsonDocument.Parse(bytes);
            if(!doc.RootElement.TryGetProperty("v4",out var entry)||doc.RootElement.EnumerateObject().Count()!=1)
                throw new IOException("Unsupported EOSH installation record schema; no game files were removed.");
            string T(string key)=>LauncherMetadataDiscovery.Text(entry,key);
            if(T("installationId")!=guid||T("artifactId")!=app||T("sandboxId")!=ns||T("itemId")!=catalog||
                !LeftoverService.EqualPath(T("dir"),location)||T("platform")!="Windows"||T("state")!="Installed"||
                !LeftoverService.EqualPath(T("manifestPath"),Path.Combine(location,".egstore",guid+".manifest")))
                throw new IOException("The EOSH record does not match this exact game installation.");
            records.Add(new(path,bytes));
        }
        if(records.Count>1)throw new IOException("Multiple EOSH installations match; automatic removal is blocked.");
        return records;
    }
    internal static bool IsRegistered(string app,string ns,string catalog,string location,IEnumerable<string>? roots=null)
    {
        foreach(string root in roots??Roots())
        {
            if(!Directory.Exists(root))continue;
            foreach(string path in Directory.EnumerateFiles(root,"*.egi",SearchOption.TopDirectoryOnly))
            {
                Physical(path);using var doc=JsonDocument.Parse(Read(path));
                if(!doc.RootElement.TryGetProperty("v4",out var e))continue;
                if(LauncherMetadataDiscovery.Text(e,"artifactId")==app&&LauncherMetadataDiscovery.Text(e,"sandboxId")==ns&&
                    LauncherMetadataDiscovery.Text(e,"itemId")==catalog&&LeftoverService.EqualPath(LauncherMetadataDiscovery.Text(e,"dir"),location))return true;
            }
        }
        return false;
    }
    internal static bool HasInstallation(InstalledProgram game)
    {
        string id=game.Identifier.StartsWith("epic:",StringComparison.OrdinalIgnoreCase)?game.Identifier[5..]:"";
        if(id.Length==0)return false;
        foreach(string root in Roots().Where(Directory.Exists))
            foreach(string path in Directory.EnumerateFiles(root,"*.egi",SearchOption.TopDirectoryOnly))
            {
                Physical(path);using var doc=JsonDocument.Parse(Read(path));if(!doc.RootElement.TryGetProperty("v4",out var e))continue;
                if(LeftoverService.EqualPath(LauncherMetadataDiscovery.Text(e,"dir"),game.Location)&&
                    (LauncherMetadataDiscovery.Text(e,"itemId")==id||LauncherMetadataDiscovery.Text(e,"artifactId")==id))return true;
            }
        return false;
    }
    internal static void Verify(IReadOnlyList<EpicEoshRecord> records)
    {foreach(var record in records){Physical(record.Path);if(!Equal(Read(record.Path),record.Bytes))throw new IOException("EOSH installation state changed; removal stopped.");}}
    internal static void Commit(IReadOnlyList<EpicEoshRecord> records,string backup,Action commitLauncher)
    {
        if(records.Count==0){commitLauncher();return;}
        var record=records.Single();string root=Path.GetDirectoryName(record.Path)!,revision=Path.Combine(root,"Revision.json"),gate=Path.Combine(root,".lock");
        Physical(gate);Physical(revision);
        // Never stop the shared EOSH service or bypass its live store lock.
        using var lease=new FileStream(gate,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        Verify(records);byte[] previous=Read(revision);var json=JsonNode.Parse(previous)!.AsObject();
        if(json.Count!=2||json["timestamp"] is not JsonValue||json["number"] is not JsonValue)throw new IOException("Unsupported EOSH revision format.");
        long number=json["number"]!.GetValue<long>();long stamp=json["timestamp"]!.GetValue<long>();
        if(number<0||stamp<0)throw new IOException("Invalid EOSH revision.");
        // timestamp is the store epoch, not the wall-clock time of each edit.
        // Preserve it and advance only the shared monotonic revision number.
        json["number"]=checked(number+1);
        byte[] next=System.Text.Encoding.UTF8.GetBytes(json.ToJsonString());
        File.WriteAllBytes(Path.Combine(backup,"EOSH-game.egi"),record.Bytes);File.WriteAllBytes(Path.Combine(backup,"EOSH-Revision.json"),previous);
        bool removed=false,changed=false;
        try
        {
            Verify(records);File.Delete(record.Path);BrowserOperationsService.VerifyAbsent(record.Path);removed=true;
            commitLauncher();
            if(!Equal(Read(revision),previous))throw new IOException("EOSH revision changed while locked.");
            Atomic(revision,next);changed=true;
            BrowserOperationsService.VerifyAbsent(record.Path);
        }
        catch
        {
            if(removed&&!File.Exists(record.Path))using(var restore=new FileStream(record.Path,FileMode.CreateNew,FileAccess.Write,FileShare.None))restore.Write(record.Bytes);
            if(changed&&Equal(Read(revision),next))Atomic(revision,previous);
            throw;
        }
    }
    private static bool Equal(byte[] a,byte[] b)=>CryptographicOperations.FixedTimeEquals(SHA256.HashData(a),SHA256.HashData(b));
    private static byte[] Read(string path){if(new FileInfo(path).Length>1024*1024)throw new IOException("EOSH metadata is too large.");return File.ReadAllBytes(path);}
    private static void Atomic(string path,byte[] bytes)
    {
        string temp=path+".polar-"+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temp,bytes);File.Replace(temp,path,null);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    private static void Physical(string path)
    {for(string? p=path;p!=null;p=Path.GetDirectoryName(p))if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("EOSH records through a junction or link are protected.");}
}

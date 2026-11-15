using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PolarUninstallerWpf.Services;

internal static class EpicDirectChecks
{
    internal static async Task<int> RunAsync()
    {
        int failed=0,count=0;void Check(bool ok,string name){count++;if(!ok)failed++;Console.WriteLine((ok?"PASS ":"FAIL ")+name);}
        string root=Path.Combine(AppContext.BaseDirectory,"fixtures","epic-direct-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            Check(EpicGameRemovalService.Enabled,"Direct Epic route is enabled in the compiled assembly");
            int closePasses=0,removalContinuations=0;bool launcherActive=true;
            bool closed=await EpicLauncherCloseService.ContinueUntilClosedAsync(()=>{closePasses++;launcherActive=closePasses<2;return Task.FromResult(true);},()=>launcherActive,CancellationToken.None);
            if(closed)removalContinuations++;
            Check(closed&&closePasses==2&&removalContinuations==1,"Parent exit plus replacement helper continues one authorized removal without a second click (simulated process lifecycle)");
            closed=await EpicLauncherCloseService.ContinueUntilClosedAsync(()=>{launcherActive=false;return Task.FromResult(false);},()=>true,CancellationToken.None);
            Check(!closed,"Unconfirmed launcher shutdown does not authorize removal");
            int escalationCount=0;var requestedStages=new List<bool>();
            closed=await EpicLauncherCloseService.CloseAuthorizedAsync(force=>{requestedStages.Add(force);return Task.FromResult(force);},()=>escalationCount++,CancellationToken.None);
            Check(closed&&escalationCount==1&&requestedStages.SequenceEqual(new[]{false,true}),"One removal authorization closes normally, then escalates once and continues without another prompt");
            requestedStages.Clear();escalationCount=0;
            closed=await EpicLauncherCloseService.CloseAuthorizedAsync(force=>{requestedStages.Add(force);return Task.FromResult(true);},()=>escalationCount++,CancellationToken.None);
            Check(closed&&escalationCount==0&&requestedStages.SequenceEqual(new[]{false}),"Successful normal close never requests forced termination");
            closePasses=0;launcherActive=true;
            closed=await EpicLauncherCloseService.ContinueUntilClosedAsync(()=>{closePasses++;launcherActive=closePasses<2;return Task.FromResult(false);},()=>launcherActive,CancellationToken.None);
            Check(closed&&closePasses==2,"A timed-out process snapshot is rechecked and replacement helpers are closed within the same operation");
            closePasses=0;
            closed=await EpicLauncherCloseService.ContinueUntilClosedAsync(()=>{closePasses++;return Task.FromResult(false);},()=>true,CancellationToken.None,1);
            Check(!closed&&closePasses==1,"Normal close has only one bounded attempt before authorized escalation");
            using(var closeCancel=new CancellationTokenSource())
            {
                closeCancel.Cancel();bool closeWasCancelled=false;
                try{await EpicLauncherCloseService.ContinueUntilClosedAsync(()=>Task.FromResult(true),()=>true,closeCancel.Token);}catch(OperationCanceledException){closeWasCancelled=true;}
                Check(closeWasCancelled,"Cancellation stops the close-and-continue sequence before removal");
            }
            // Read-only inspection of the user's explicitly designated game. No
            // RemoveAsync call ever receives this plan or installation directory.
            var real=new InstalledProgram("Scourgebringer","Epic Games","1.61.0_win-x64",@"C:\Program Files\Epic Games\Scourgebringer",Source:"Epic Games",Identifier:"epic:4c0313f2f6b9409bb8974948c2dabcfc");
            if(Directory.Exists(real.Location))
            {
                var plan=await new EpicGameRemovalService().PrepareAsync(real,new[]{real},CancellationToken.None);
                Check(plan.AppName=="bf363d9625a5406f904d925f3e613327"&&plan.Files.Count>0,"Read-only ScourgeBringer item/build identity and file list agree");
                int verified=0,missing=0,changed=0;
                foreach(var file in plan.Files)
                {
                    string path=Path.Combine(plan.Root,file.RelativePath);if(!File.Exists(path)){missing++;continue;}
                    using var input=File.OpenRead(path);if(input.Length==file.Size&&Convert.ToHexString(await SHA1.HashDataAsync(input)).Equals(file.Sha1,StringComparison.OrdinalIgnoreCase))verified++;else changed++;
                }
                Check(changed==0&&verified>0,$"Read-only real payload check: {verified} original files, {missing} absent, {changed} changed; no deletion");
            }
            else
            {
                string? item=Directory.EnumerateFiles(@"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests","*.item").FirstOrDefault(path=>{using var doc=JsonDocument.Parse(File.ReadAllBytes(path));return doc.RootElement.GetProperty("AppName").GetString()=="bf363d9625a5406f904d925f3e613327";});
                if(item!=null)
                {
                    var service=new EpicGameRemovalService(()=>new[]{item},()=>new[]{@"C:\ProgramData\Epic\UnrealEngineLauncher\LauncherInstalled.dat"},()=>false,@"C:\Users\qmc\AppData\Local\PolarUninstallerWPF-Development\GameRemovalBackups\Epic");
                    var plan=await service.PrepareAsync(real,new[]{real},CancellationToken.None);
                    Check(plan.RecordsOnly&&plan.Files.Count==81&&service.RecoverableGames().Count==1,"Read-only ScourgeBringer stale record matches its authorized backup; no records changed");
                    Check(plan.Eosh?.Count==1&&EpicEoshStore.HasInstallation(real),"Read-only diagnosis confirms the exact EOSH record still registers ScourgeBringer");
                }
            }
            var shortId=Fixture(Path.Combine(root,"short-id"),installationId:new string('A',31));
            var shortPlan=await shortId.Service.PrepareAsync(shortId.Game,new[]{shortId.Game},CancellationToken.None);
            var shortResult=await shortId.Service.RemoveAsync(shortPlan,new[]{shortId.Game},null,CancellationToken.None);
            Check(shortResult.Confirmed&&!File.Exists(shortPlan.ItemPath)&&shortPlan.Eosh!.Single().Path.EndsWith(new string('A',31)+".egi"),"Epic non-padded 31-character installation ID preserves exact manifest/EOSH identity and removes only its disposable payload");
            var success=Fixture(Path.Combine(root,"success"));
            var own=await success.Service.PrepareAsync(success.Game,new[]{success.Game},CancellationToken.None);
            var outcome=await success.Service.RemoveAsync(own,new[]{success.Game},null,CancellationToken.None);
            Check(outcome.Confirmed&&!File.Exists(Path.Combine(own.Root,"Game.exe"))&&!File.Exists(Path.Combine(own.Root,"asset.bin")),"Actual removal deletes only the disposable manifest-listed payload");
            Check(File.ReadAllText(Path.Combine(own.Root,"saves","slot.sav"))=="test-save"&&File.ReadAllText(Path.Combine(own.Root,"personal.txt"))=="test-personal","Saves and unlisted personal files survive actual cleanup");
            Check(!File.Exists(own.ItemPath)&&File.Exists(Path.Combine(outcome.Backup,"Epic.item"))&&File.Exists(Path.Combine(outcome.Backup,"Build.manifest")),"Exact Epic records removed with byte-for-byte metadata backup");
            using(var index=JsonDocument.Parse(File.ReadAllText(success.Index)))Check(index.RootElement.GetProperty("InstallationList").GetArrayLength()==1&&index.RootElement.GetProperty("UnknownProperty").GetString()=="keep","Shared index preserves the other game and unknown fields");
            Check(File.ReadAllBytes(Path.Combine(outcome.Backup,"Epic.item")).SequenceEqual(own.ItemBytes),"Backup exactly matches the captured item manifest");
            Check(own.Eosh?.Count==1&&!File.Exists(own.Eosh[0].Path)&&File.Exists(Path.Combine(outcome.Backup,"EOSH-game.egi")),"Captured EOSH game record is backed up and removed");
            string eoshRoot=Path.Combine(Path.GetDirectoryName(success.Index)!,"Eosh","InstalledItems");
            using(var revision=JsonDocument.Parse(File.ReadAllText(Path.Combine(eoshRoot,"Revision.json"))))Check(revision.RootElement.GetProperty("timestamp").GetInt64()==100&&revision.RootElement.GetProperty("number").GetInt64()==4,"EOSH revision advances while its store epoch remains unchanged");
            Check(File.ReadAllText(Path.Combine(eoshRoot,"other.egi"))=="{\"v4\":{\"artifactId\":\"another-game\"}}","Other EOSH game records remain byte-for-byte unchanged");
            Check(EpicLauncherCloseService.OwnsImage(@"C:\Epic\Launcher\Engine\Binaries\Win64\EpicWebHelper.exe",@"C:\Epic\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe")&&
                !EpicLauncherCloseService.OwnsImage(@"C:\Unrelated\EpicWebHelper.exe",@"C:\Epic\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"),"Launcher closing rejects same-named executables outside the verified install tree");
            Directory.Delete(own.Root,true); // Only this owned fixture's retained files.
            File.WriteAllBytes(own.ItemPath,own.ItemBytes); // Simulate a stale launcher cache recreating its record.
            File.WriteAllBytes(own.Eosh!.Single().Path,own.Eosh.Single().Bytes);
            var recovery=await success.Service.PrepareAsync(success.Game,new[]{success.Game},CancellationToken.None);
            Check(recovery.RecordsOnly&&success.Service.RecoverableGames().Count==1,"Previously authorized removal can expose a recreated stale record for recovery");
            var repaired=await success.Service.RemoveAsync(recovery,new[]{success.Game},null,CancellationToken.None);
            Check(repaired.Confirmed&&repaired.Removed==0&&!File.Exists(own.ItemPath),"Stale-record recovery removes only its exact registration; no payload is deleted twice");
            var modified=Fixture(Path.Combine(root,"modified"));var changedPlan=await modified.Service.PrepareAsync(modified.Game,new[]{modified.Game},CancellationToken.None);
            File.AppendAllText(Path.Combine(changedPlan.Root,"asset.bin"),"changed");bool blocked=false;
            try{await modified.Service.RemoveAsync(changedPlan,new[]{modified.Game},null,CancellationToken.None);}catch(IOException){blocked=true;}
            Check(blocked&&File.Exists(changedPlan.ItemPath)&&File.Exists(changedPlan.LaunchPath),"Modified file aborts before deleting payload or registration");
            var locked=Fixture(Path.Combine(root,"locked"));var lockedPlan=await locked.Service.PrepareAsync(locked.Game,new[]{locked.Game},CancellationToken.None);blocked=false;
            using(var hold=new FileStream(Path.Combine(lockedPlan.Root,"asset.bin"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {try{await locked.Service.RemoveAsync(lockedPlan,new[]{locked.Game},null,CancellationToken.None);}catch(IOException){blocked=true;}}
            Check(blocked&&File.Exists(lockedPlan.ItemPath)&&File.Exists(lockedPlan.LaunchPath),"Locked payload does not invalidate Epic registration");
            var running=Fixture(Path.Combine(root,"running"),true);var runningPlan=await running.Service.PrepareAsync(running.Game,new[]{running.Game},CancellationToken.None);blocked=false;
            try{await running.Service.RemoveAsync(runningPlan,new[]{running.Game},null,CancellationToken.None);}catch(IOException){blocked=true;}
            Check(blocked&&File.Exists(runningPlan.LaunchPath)&&File.Exists(runningPlan.ItemPath),"Running-launcher guard blocks removal (simulated launcher state)");
            var stale=Fixture(Path.Combine(root,"stale"));var stalePlan=await stale.Service.PrepareAsync(stale.Game,new[]{stale.Game},CancellationToken.None);File.AppendAllText(stalePlan.ItemPath," ");blocked=false;
            try{await stale.Service.RemoveAsync(stalePlan,new[]{stale.Game},null,CancellationToken.None);}catch(IOException){blocked=true;}
            Check(blocked&&File.Exists(stalePlan.LaunchPath),"Changed item fingerprint blocks stale plan execution");
            var cancelled=Fixture(Path.Combine(root,"cancelled"));var cancelPlan=await cancelled.Service.PrepareAsync(cancelled.Game,new[]{cancelled.Game},CancellationToken.None);using var cts=new CancellationTokenSource();cts.Cancel();blocked=false;
            try{await cancelled.Service.RemoveAsync(cancelPlan,new[]{cancelled.Game},null,cts.Token);}catch(OperationCanceledException){blocked=true;}
            Check(blocked&&File.Exists(cancelPlan.ItemPath)&&File.Exists(cancelPlan.LaunchPath),"Cancellation before removal preserves payload and registration");
            byte[] corrupt=own.BuildBytes.ToArray();corrupt[^1]^=0x7f;blocked=false;try{EpicBuildManifestReader.Read(corrupt);}catch(InvalidDataException){blocked=true;}
            Check(blocked,"Corrupt build metadata is rejected");
            blocked=false;try{EpicBuildManifestReader.Read(Build("bad",new Dictionary<string,byte[]>{{@"..\outside.txt",new byte[]{1}}}));}catch(InvalidDataException){blocked=true;}
            Check(blocked,"Manifest traversal outside the selected game is rejected");
            blocked=false;try{EpicBuildManifestReader.Read(Build("bad",new Dictionary<string,byte[]>{{"NUL.txt",new byte[]{1}}}));}catch(InvalidDataException){blocked=true;}
            Check(blocked,"Windows device names cannot become removal targets");
            var rollback=Fixture(Path.Combine(root,"eosh-rollback"));var rollbackPlan=await rollback.Service.PrepareAsync(rollback.Game,new[]{rollback.Game},CancellationToken.None);
            string rollbackBackup=Path.Combine(root,"eosh-rollback-backup");Directory.CreateDirectory(rollbackBackup);blocked=false;
            try{EpicEoshStore.Commit(rollbackPlan.Eosh!,rollbackBackup,()=>throw new IOException("Disposable commit failure"));}catch(IOException){blocked=true;}
            Check(blocked&&File.ReadAllBytes(rollbackPlan.Eosh!.Single().Path).SequenceEqual(rollbackPlan.Eosh.Single().Bytes),"EOSH record is restored if launcher metadata commit fails");
            string rollbackStore=Path.GetDirectoryName(rollbackPlan.Eosh!.Single().Path)!;
            using(var hold=new FileStream(Path.Combine(rollbackStore,".lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                blocked=false;try{EpicEoshStore.Commit(rollbackPlan.Eosh!,rollbackBackup,()=>{});}catch(IOException){blocked=true;}
                Check(blocked&&File.Exists(rollbackPlan.Eosh.Single().Path),"A busy EOSH store lock blocks changes rather than bypassing the shared service");
            }
        }
        finally{if(!root.StartsWith(Path.Combine(AppContext.BaseDirectory,"fixtures","epic-direct-"),StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid fixture scope");Directory.Delete(root,true);}
        Console.WriteLine($"{count} focused Epic checks, {failed} failures. Only disposable fixture payloads were deleted; ScourgeBringer was read only.");return failed==0?0:1;
    }
    private sealed record TestInstallation(EpicGameRemovalService Service,InstalledProgram Game,string Index);
    private static TestInstallation Fixture(string root,bool running=false,string? installationId=null)
    {
        string gameRoot=Path.Combine(root,"Game"),metadata=Path.Combine(gameRoot,".egstore"),records=Path.Combine(root,"Manifests");Directory.CreateDirectory(metadata);Directory.CreateDirectory(records);Directory.CreateDirectory(Path.Combine(gameRoot,"saves"));
        var data=new Dictionary<string,byte[]>{{"Game.exe",Encoding.UTF8.GetBytes("disposable-executable")},{"asset.bin",Encoding.UTF8.GetBytes("disposable-asset")},{@"saves\slot.sav",Encoding.UTF8.GetBytes("test-save")}};
        foreach(var item in data)File.WriteAllBytes(Path.Combine(gameRoot,item.Key),item.Value);File.WriteAllText(Path.Combine(gameRoot,"personal.txt"),"test-personal");
        string app="fixture-app",catalog="fixture-catalog",guid=installationId??Guid.NewGuid().ToString("N"),itemPath=Path.Combine(records,guid+".item"),index=Path.Combine(root,"LauncherInstalled.dat");
        File.WriteAllBytes(Path.Combine(metadata,guid+".manifest"),Build(app,data));
        File.WriteAllText(itemPath,JsonSerializer.Serialize(new{AppName=app,CatalogItemId=catalog,CatalogNamespace="fixture-namespace",InstallationGuid=guid,InstallLocation=gameRoot,LaunchExecutable="Game.exe",bIsIncompleteInstall=false}));
        File.WriteAllText(index,JsonSerializer.Serialize(new{UnknownProperty="keep",InstallationList=new[]{new{AppName=app,InstallLocation=gameRoot},new{AppName="other-game",InstallLocation=Path.Combine(root,"Unrelated")}}}));
        var game=new InstalledProgram("Disposable Epic Game","Epic Games","1",gameRoot,Source:"Epic Games",Identifier:"epic:"+catalog);
        string eosh=Path.Combine(root,"Eosh","InstalledItems");Directory.CreateDirectory(eosh);File.WriteAllBytes(Path.Combine(eosh,".lock"),Array.Empty<byte>());File.WriteAllText(Path.Combine(eosh,"Revision.json"),"{\"timestamp\":100,\"number\":3}");
        File.WriteAllText(Path.Combine(eosh,guid+".egi"),JsonSerializer.Serialize(new{v4=new{installationId=guid,artifactId=app,sandboxId="fixture-namespace",itemId=catalog,dir=gameRoot,manifestPath=Path.Combine(metadata,guid+".manifest"),platform="Windows",state="Installed"}}));
        File.WriteAllText(Path.Combine(eosh,"other.egi"),"{\"v4\":{\"artifactId\":\"another-game\"}}");
        return new(new EpicGameRemovalService(()=>Directory.GetFiles(records,"*.item"),()=>new[]{index},()=>running,Path.Combine(root,"Backups"),new[]{eosh}),game,index);
    }
    private static byte[] Build(string app,IReadOnlyDictionary<string,byte[]> files)
    {
        using var data=new MemoryStream();using var w=new BinaryWriter(data,Encoding.UTF8,true);
        void Text(string s){if(s.Length==0){w.Write(0);return;}byte[] b=Encoding.UTF8.GetBytes(s);w.Write(b.Length+1);w.Write(b);w.Write((byte)0);}
        long Section(){long start=data.Position;w.Write(0u);return start;}void End(long start){long end=data.Position;data.Position=start;w.Write((uint)(end-start));data.Position=end;}
        long part=Section();w.Write((byte)0);w.Write(17u);w.Write((byte)0);w.Write(0u);Text(app);Text("1");Text("Game.exe");End(part);
        part=Section();w.Write((byte)0);w.Write(0u);End(part);
        part=Section();w.Write((byte)0);w.Write((uint)files.Count);foreach(var item in files)Text(item.Key);foreach(var item in files)Text("");foreach(var item in files)w.Write(SHA1.HashData(item.Value));foreach(var item in files)w.Write((byte)0);foreach(var item in files)w.Write(0u);
        foreach(var item in files){w.Write(1u);w.Write(28u);w.Write(new byte[16]);w.Write(0u);w.Write((uint)item.Value.Length);}End(part);
        byte[] body=data.ToArray();using var compressed=new MemoryStream();using(var zip=new ZLibStream(compressed,CompressionLevel.Fastest,true))zip.Write(body);
        using var output=new MemoryStream();using var header=new BinaryWriter(output);header.Write(0x44BEC00Cu);header.Write(41u);header.Write((uint)body.Length);header.Write((uint)compressed.Length);header.Write(SHA1.HashData(body));header.Write((byte)1);header.Write(17u);header.Write(compressed.ToArray());return output.ToArray();
    }
}

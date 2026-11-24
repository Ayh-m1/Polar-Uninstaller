using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PolarUninstallerWpf.Services;

internal static class LauncherExpansionChecks
{
    internal static int VerifyRelease(string exe,string expectedVersion="1.0.141",int entryCount=8)
    {
        int failed=0;
        void Check(bool ok,string message){if(!ok)failed++;Console.WriteLine((ok?"PASS ":"FAIL ")+message);}
        Check(AppInfo.Version==expectedVersion,"Compiled application identity is v"+expectedVersion);
        Check(EpicGameRemovalService.Enabled&&LauncherRemovalCoordinator.Enabled,"Default production assembly enables both removal engines without trial defines");
        using var payload=File.OpenRead(exe);string hash=Convert.ToHexString(SHA256.HashData(payload));
        var verification=UpdateVerificationService.VerifyStandaloneExecutable(exe,expectedVersion,hash,payload.Length);
        Check(verification.IsValid,"Final standalone EXE passes the existing updater version, hash and executable checks: "+verification.Error);
        var assembly=typeof(AppInfo).Assembly;
        using var stream=assembly.GetManifestResourceStream(assembly.GetName().Name+".g.resources")!;
        using var resources=new System.Resources.ResourceReader(stream);
        var entries=resources.Cast<System.Collections.DictionaryEntry>().ToArray();
        var notes=(Stream)entries.Single(e=>string.Equals(e.Key.ToString(),"assets/releasenotes.json",StringComparison.OrdinalIgnoreCase)).Value!;
        using var json=JsonDocument.Parse(notes);var current=json.RootElement[0];
        Check(current.GetProperty("version").GetString()==expectedVersion&&current.GetProperty("en").GetArrayLength()==entryCount&&current.GetProperty("ar").GetArrayLength()==entryCount,"Embedded What's New contains all English and Arabic v"+expectedVersion+" entries");
        Check(json.RootElement[1].GetProperty("version").GetString()==(expectedVersion=="1.0.142"?"1.0.141":"1.0.140"),"Previous release history remains embedded");
        Check(entries.Any(e=>e.Key.ToString()=="assets/polar.ico")&&entries.Any(e=>e.Key.ToString()=="assets/polarlogo.png"),"Official icon and logo are embedded");
        Check(entries.Any(e=>e.Key.ToString()!.Contains("inter",StringComparison.OrdinalIgnoreCase)&&e.Key.ToString()!.EndsWith(".ttf"))&&entries.Any(e=>e.Key.ToString()!.Contains("tajawal",StringComparison.OrdinalIgnoreCase)&&e.Key.ToString()!.EndsWith(".ttf")),"Inter and Tajawal fonts are embedded");
        if(expectedVersion=="1.0.142")
        {
            var iconStream=(Stream)entries.Single(e=>e.Key.ToString()=="assets/polar.ico").Value!;
            using var copy=new MemoryStream();iconStream.CopyTo(copy);byte[] icon=copy.ToArray();
            int[] sizes={16,24,32,48,64,128,256};bool valid=BitConverter.ToUInt16(icon,4)==sizes.Length;
            for(int i=0;i<sizes.Length;i++)
            {
                int size=sizes[i],offset=BitConverter.ToInt32(icon,6+i*16+12),stride=size*4;
                valid&=(icon[6+i*16]==0?256:icon[6+i*16])==size&&BitConverter.ToInt32(icon,offset)==40&&BitConverter.ToUInt16(icon,offset+14)==32;
                valid&=icon[offset+40+3]==0&&icon[offset+40+(size-1)*4+3]==0&&icon[offset+40+(size-1)*stride+3]==0;
                valid&=Enumerable.Range(0,size*size).Any(p=>icon[offset+40+p*4+3] is >0 and <255);
            }
            Check(valid,"All seven embedded Windows icon frames have transparent rounded corners and antialiased alpha edges");
            string project=AppContext.BaseDirectory;
            while(!File.Exists(Path.Combine(project,"PolarUninstallerWpf.csproj")))project=Directory.GetParent(project)?.FullName??throw new IOException("Project root missing");
            Check(icon.SequenceEqual(File.ReadAllBytes(Path.Combine(project,"Assets","Polar.ico"))),"Embedded application icon exactly matches the official asset used by NSIS");
        }
        Console.WriteLine("Release metadata/resource checks: "+failed+" failures. No UI or installed application was launched.");return failed==0?0:1;
    }
    internal static async Task<int> AuditAsync()
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var games=await new LauncherInventoryService(Console.WriteLine).GetInstalledAsync(deadline.Token);
        var windows=await new WindowsInventoryService().GetInstalledAsync(deadline.Token);
        var inventory=InventoryMerger.Merge(windows.Concat(games));
        foreach(string prefix in new[]{"epic:"}.Concat(LauncherRemovalCoordinator.Profiles.Select(p=>p.Prefix)))
        {
            var installed=inventory.Where(p=>p.Identifier.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToArray();
            Console.WriteLine("COVERAGE "+prefix+" "+installed.Length+" installed games");
            foreach(var game in installed)
            {
                var decision=SmartUninstallSelectionService.Evaluate(game,inventory);
                Console.WriteLine("  "+game.Name+" | "+game.Identifier+" | "+decision.Method+" | "+(decision.GameRoute?.Target??decision.ReasonEn));
                if(decision.GameRoute?.DirectEpic==true)
                {
                    try {var plan=await new EpicGameRemovalService().PrepareAsync(game,inventory,deadline.Token);Console.WriteLine("    READ ONLY plan: "+plan.Files.Count+" manifest files");}
                    catch(Exception e)when(LauncherMetadataDiscovery.ReadFailure(e)){Console.WriteLine("    BLOCKED: "+e.Message);}
                }
            }
        }
        Console.WriteLine("Read-only coverage audit. Steam excluded; no launcher closed, command launched, or installed file changed.");
        return 0;
    }
    internal static int RemoveFixture(string path)
    {
        string root=Path.GetFullPath(path),allowed=Path.Combine(AppContext.BaseDirectory,"fixtures","launcher-expansion-");
        if(!root.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||!File.Exists(Path.Combine(root,"owned-fixture.marker")))return 2;
        File.Delete(Path.Combine(root,"Game.exe"));return File.Exists(Path.Combine(root,"Game.exe"))?1:0;
    }
    internal static async Task<int> RunAsync()
    {
        int failed=0,count=0;
        void Check(bool pass,string name){count++;if(!pass)failed++;Console.WriteLine((pass?"PASS ":"FAIL ")+name);}
        string root=Path.Combine(AppContext.BaseDirectory,"fixtures","launcher-expansion-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            Check(LauncherRemovalCoordinator.Enabled&&EpicGameRemovalService.Enabled,"Both launcher adapters and generic Epic engine are enabled");
            var steam=new InstalledProgram("Fixture Steam","Valve","1",root,Source:"Steam",Identifier:"steam:123");
            Check(!LauncherRemovalCoordinator.Supports(steam)&&LauncherRemovalCoordinator.Profile(steam)==null&&LauncherGameCommands.Resolve(steam)==null,"Steam is excluded from all new command and shutdown adapters");
            string testExe=Path.Combine(AppContext.BaseDirectory,"SteamUninstallRegression.exe");Check(File.Exists(testExe),"Disposable native-uninstaller test host exists");
            foreach(var profile in LauncherRemovalCoordinator.Profiles)
            {
                string folder=Path.Combine(root,profile.Prefix.TrimEnd(':'));Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"owned-fixture.marker"),"owned");File.WriteAllText(Path.Combine(folder,"Game.exe"),"fixture payload");File.WriteAllText(Path.Combine(folder,"save.sav"),"private fixture save");
                string id=profile.Prefix+(profile.Prefix=="gog:"?"9876543210987":"fixture-product");
                var game=new InstalledProgram("Disposable "+profile.Name,"Fixture","1",folder,Uninstall:"\""+testExe+"\" --launcher-fixture-uninstall \""+folder+"\"",Source:profile.Prefix=="ea:"?"EA":profile.Name,Identifier:id,RequiresOfficialRemoval:true);
                var route=SmartUninstallSelectionService.ResolveGame(game);
                Check(route is {CloseLauncher:true,TrackProcess:true,ManualInteraction:false}&&route.Target==testExe,profile.Name+" selects exact native game removal with close-and-continue flags");
                string launcher=Path.Combine(folder,profile.ProcessNames[0]+".exe");
                Check(LauncherRemovalCoordinator.OwnsImage(profile,launcher,launcher)&&!LauncherRemovalCoordinator.OwnsImage(profile,Path.Combine(root,"unrelated",profile.ProcessNames[0]+".exe"),launcher),profile.Name+" process ownership protects same-named unrelated applications");
                var registration=new RegistryLeftover(Microsoft.Win32.RegistryHive.CurrentUser,Microsoft.Win32.RegistryView.Registry64,@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PolarDisposable-"+Guid.NewGuid().ToString("N"),game.Name,folder,game.Uninstall);
                var snapshot=new UninstallSnapshot(game,new[]{registration},Array.Empty<ShortcutLeftover>(),Array.Empty<OwnedDataFolder>(),new[]{Path.Combine(folder,"Game.exe")});
                Check(!UninstallMonitorService.Probe(game,snapshot).Confirmed,profile.Name+" cannot report removal while the captured game executable remains");
                Check(LauncherGameCommands.Resolve(game with{Uninstall=""})==null,profile.Name+" cannot invent an uninstall route without game-specific evidence");
                var stages=new List<bool>();int escalations=0,continuations=0;
                bool closed=await EpicLauncherCloseService.CloseAuthorizedAsync(force=>{stages.Add(force);return Task.FromResult(force);},()=>escalations++,CancellationToken.None);
                if(closed)continuations++;
                Check(stages.SequenceEqual(new[]{false,true})&&escalations==1&&continuations==1,profile.Name+" uses one authorization for close, escalation and one removal continuation (simulated launcher)");
                using var child=Process.Start(new ProcessStartInfo(route!.Target){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"--launcher-fixture-uninstall",folder}})!;
                await child.WaitForExitAsync();
                var probe=UninstallMonitorService.Probe(game,snapshot);
                if(!probe.Confirmed)Console.WriteLine("DETAIL "+profile.Name+": exit="+child.ExitCode+"; mainExists="+File.Exists(Path.Combine(folder,"Game.exe"))+"; "+probe.Detail);
                Check(child.ExitCode==0&&probe.Confirmed&&!File.Exists(Path.Combine(folder,"Game.exe"))&&File.ReadAllText(Path.Combine(folder,"save.sav"))=="private fixture save",profile.Name+" actual disposable uninstall process exits, removal verifies, and save remains");
            }
            string gog=Path.Combine(root,"gog-native");Directory.CreateDirectory(gog);File.WriteAllText(Path.Combine(gog,"goggame-123.info"),"{\"gameId\":\"123\"}");File.WriteAllText(Path.Combine(gog,"unins000.exe"),"not executed");File.WriteAllText(Path.Combine(gog,"unins000.dat"),"fixture");
            var gogGame=new InstalledProgram("Disposable GOG","GOG","1",gog,Source:"GOG",Identifier:"gog:123");
            Check(LauncherGameCommands.ResolveGogPayload(gogGame)?.CloseLauncher==true,"Verified GOG payload can resolve its own uninstaller without ARP registration");
            Check(LauncherGameCommands.ResolveGogPayload(gogGame with{Identifier="gog:456"})==null,"Mismatched GOG ID cannot reuse another game's uninstaller");
            Check(!LauncherGameCommands.Route(@"C:\Riot Client\RiotClientServices.exe",new[]{"--uninstall-product=fixture"}).TrackProcess,"Persistent launcher processes are verified by installation state rather than waiting forever for client exit");
            string xml=Path.Combine(root,"installerdata.xml");File.WriteAllText(xml,"<game><gameIDs><gameID>Origin.OFR.50.Fixture</gameID></gameIDs></game>");
            Check(EaGameDiscovery.ProductId(xml)=="Origin.OFR.50.Fixture","EA installer XML supplies a real product identity");
            File.WriteAllText(xml,"<!DOCTYPE game [<!ENTITY x SYSTEM 'file:///C:/private'>]><game><gameID>&x;</gameID></game>");bool xmlBlocked=false;try{EaGameDiscovery.ProductId(xml);}catch(System.Xml.XmlException){xmlBlocked=true;}
            Check(xmlBlocked,"EA XML external entities are blocked");
            string bnetExe=Path.Combine(root,"Blizzard Uninstaller.exe"),riotExe=Path.Combine(root,"RiotClientServices.exe");
            File.WriteAllText(bnetExe,"unexecuted parser fixture");File.WriteAllText(riotExe,"unexecuted parser fixture");
            var bnet=new InstalledProgram("Fixture Battle.net","Blizzard","1",Path.Combine(root,"battlenet"),Source:"Battle.net",Identifier:"battlenet:fixture-product");
            string bnetCommand="\""+bnetExe+"\" --uid=fixture-product";
            Check(LauncherGameCommands.RegistrationMatches(bnet,"anything","Localized title","",bnetCommand),"Battle.net exact Agent UID resolves a registration without InstallLocation");
            Check(!LauncherGameCommands.RegistrationMatches(bnet,"anything",bnet.Name,"",bnetCommand.Replace("fixture-product","another-product")),"Battle.net same title cannot substitute another product UID");
            Check(!LauncherGameCommands.RegistrationMatches(bnet,"anything",bnet.Name,Path.Combine(root,"other-game"),bnetCommand),"Battle.net conflicting installation path cannot be overridden by a product argument");
            Check(!LauncherGameCommands.RegistrationMatches(bnet,"anything",bnet.Name,"",bnetCommand+" --uid=other"),"Ambiguous duplicate Battle.net UID arguments are rejected");
            var riot=bnet with{Name="Disposable Riot",Identifier="riot:valorant",Source="Riot Games"};
            string riotCommand="\""+riotExe+"\" --uninstall-product=valorant --uninstall-patchline=live";
            Check(LauncherGameCommands.RegistrationMatches(riot,"anything","Localized title","",riotCommand),"Riot exact registered product resolves without InstallLocation");
            Check(!LauncherGameCommands.RegistrationMatches(riot,"anything",riot.Name,"",riotCommand.Replace("valorant","league_of_legends")),"A Riot game's registration cannot target another Riot product");
            Check(LauncherGameCommands.RegistrationMatches(gogGame,"GOG Game 123","Localized title","",""),"GOG exact product key can identify an installation without ARP path");
            Check(!LauncherGameCommands.RegistrationMatches(gogGame,"GOG Game 456",gogGame.Name,"",""),"GOG title alone cannot match another product key");
            var uplay=gogGame with{Identifier="ubisoft:123",Source="Ubisoft"};
            Check(LauncherGameCommands.RegistrationMatches(uplay,"Uplay Install 123","Localized title","",""),"Ubisoft exact registered product key is recognized");
            Check(!LauncherGameCommands.RegistrationMatches(uplay,"Uplay Install 456",uplay.Name,"",""),"Ubisoft title alone cannot match another product key");
            // Read installed Epic build manifests only. Never hash or remove real payloads here.
            foreach(string item in LauncherMetadataDiscovery.EpicManifestFiles())
            {
                using var json=JsonDocument.Parse(File.ReadAllBytes(item));var e=json.RootElement;
                string file=LauncherMetadataDiscovery.Text(e,"CompleteManifestPath");if(!File.Exists(file))continue;
                var build=EpicBuildManifestReader.Read(File.ReadAllBytes(file));
                var game=new InstalledProgram(LauncherMetadataDiscovery.Text(e,"DisplayName"),"Epic Games",LauncherMetadataDiscovery.Text(e,"AppVersionString"),LauncherMetadataDiscovery.Text(e,"InstallLocation"),Source:"Epic Games",Identifier:"epic:"+LauncherMetadataDiscovery.Text(e,"CatalogItemId"));
                var plan=await new EpicGameRemovalService().PrepareAsync(game,new[]{game},CancellationToken.None);
                Check(plan.Files.Count==build.Files.Count&&plan.Files.Count>0,"Read-only complete Epic removal plan: "+game.Name+" / "+build.Files.Count+" files; artifact/build alias bound by EOSH version and executable");
            }
            string project=AppContext.BaseDirectory;while(!File.Exists(Path.Combine(project,"PolarUninstallerWpf.csproj")))project=Directory.GetParent(project)?.FullName??throw new IOException("Project root missing");
            string original=Path.Combine(project,"Build","Backups","LauncherExpansion-20261113-135901");
            string Extract(string path,string signature)
            {
                string text=File.ReadAllText(path);int start=text.IndexOf(signature,StringComparison.Ordinal),brace=text.IndexOf('{',start),depth=1,end=brace+1;
                while(depth>0&&end<text.Length){if(text[end]=='{')depth++;if(text[end]=='}')depth--;end++;}return text[start..end];
            }
            foreach(var pair in new[]{("Services/LauncherInventoryService.cs","internal static void ScanSteam("),("Services/UninstallMonitorService.cs","private static IEnumerable<string> SteamRoots(")})
            {
                string before=Extract(Path.Combine(original,pair.Item1.Replace('/','_')+".bak"),pair.Item2),after=Extract(Path.Combine(project,pair.Item1),pair.Item2);
                Check(before==after,"Unchanged Steam method: "+pair.Item2);
            }
            string retained=Directory.CreateDirectory(Path.Combine(root,"steam-fixture","steamapps","common","Retained Images")).FullName;
            string image=Path.Combine(retained,"cached.png");
            File.WriteAllText(image,"fixture");
            var removedSteam=new InstalledProgram("Retained Images","Fixture","1",retained,Source:"Steam",Identifier:"steam:991501502");
            Check(UninstallMonitorService.Probe(removedSteam,null) is { Confirmed:true,RetainedSteamAssets:true }&&File.Exists(image),
                "Steam verification recognizes an unregistered image-only folder and preserves its files");
        }
        finally{if(!root.StartsWith(Path.Combine(AppContext.BaseDirectory,"fixtures","launcher-expansion-"),StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid fixture scope");Directory.Delete(root,true);}
        Console.WriteLine($"{count} focused launcher checks, {failed} failures. Real launchers were not closed and no installed game was uninstalled. Native executions used disposable fixture stubs only.");return failed==0?0:1;
    }
}

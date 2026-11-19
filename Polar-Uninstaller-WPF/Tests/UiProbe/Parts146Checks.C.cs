using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PolarUninstallerWpf.Services;
internal static partial class Parts146Checks
{
    internal static int RunC()
    {
        Setup("C");ProgramIconDiskCache.UseCacheDirectory(Path.Combine(root,"icons"));
        IconWorkerPool.ConfigureExecutable(Path.Combine(project,"bin","Release","net9.0-windows","win-x64","PolarUninstaller.dll"));
        foreach(var sample in new[]{(@"C:\a\b.exe,0",@"C:\a\b.exe",0),(@"""C:\a b\c.exe"",-101",@"C:\a b\c.exe",-101),(@"%SystemRoot%\x.dll,3",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"x.dll"),3)})
            Check(LocalIconSources.TryParseDisplayIcon(sample.Item1,out var parsed,out int index)&&parsed==sample.Item2&&index==sample.Item3,"DisplayIcon handles "+sample.Item1);
        foreach(string invalid in new[]{@"\\server\share\app.exe","https://example.com/icon.png","relative.exe",@"C:\bad.exe,99999999999999"})
            Check(!LocalIconSources.TryParseDisplayIcon(invalid,out _,out _),"Reject unsafe/invalid icon source "+invalid);
        string install=Path.Combine(root,"Fixture");Directory.CreateDirectory(install);string png=Path.Combine(install,"Fixture.png");
        File.Copy(Path.Combine(project,"Assets","PolarLogo.png"),png);
        File.Copy(Path.Combine(project,"bin","Release","net9.0-windows","win-x64","PolarUninstaller.exe"),Path.Combine(install,"Fixture.exe"));
        File.Copy(Path.Combine(install,"Fixture.exe"),Path.Combine(install,"uninstall.exe"));
        File.WriteAllText(Path.Combine(install,"AppxManifest.xml"),"<Package><Applications><Application><VisualElements Square44x44Logo='Fixture.png'/></Application></Applications></Package>");
        var fixture=new InstalledProgram("Fixture","Fixture","1",install,png,Uninstall:"\""+Path.Combine(install,"uninstall.exe")+"\""){RegisteredIcon=png};
        var candidates=LocalIconSources.Candidates(fixture,64).ToArray();
        Check(candidates.Select(c=>c.Method).Take(3).SequenceEqual(new[]{IconResolutionMethod.Registry,IconResolutionMethod.Executable,IconResolutionMethod.Uninstaller}),"Regular chain starts Registry -> best main EXE -> owned uninstaller");
        Check(candidates.First(c=>c.Method==IconResolutionMethod.Executable).Path.EndsWith("Fixture.exe"),"Main EXE excludes uninstaller");
        Check(LocalIconSources.StoreIcons(fixture,64).Contains(png),"Store manifest resolves owned local asset");
        // Regression: Steam must never adopt the Microsoft Teams package logo.
        string fakeApps=Path.Combine(root,"WindowsApps");
        string teamsRoot=Path.Combine(fakeApps,"MSTeams_26276.513.5241.9675_x64__8wekyb3d8bbwe");
        string teamsImage=Path.Combine(teamsRoot,"Images","TeamsForWorkNewStoreLogo.png");
        Directory.CreateDirectory(Path.Combine(teamsRoot,"Images"));File.Copy(png,teamsImage);
        File.WriteAllText(Path.Combine(teamsRoot,"AppxManifest.xml"),"<Package><Applications><Application><VisualElements Square44x44Logo='Images/TeamsForWorkNewStoreLogo.png'/></Application></Applications></Package>");
        var steamEntry=new InstalledProgram("Steam","Valve Corporation","2.10.91.91");
        Check(LocalIconSources.StoreRootFor(steamEntry,fakeApps)==""&&LocalIconSources.StoreIcons(steamEntry,64).Count==0,"Steam never matches the Teams MSIX package by name substring");
        var teamsEntry=new InstalledProgram("Microsoft Teams","Microsoft","1",Path.Combine(teamsRoot,"Images"));
        Check(LocalIconSources.StoreRootFor(teamsEntry,fakeApps)==teamsRoot,"Only the entry's exact package root is accepted");
        var teamsOwned=teamsEntry with{Location=teamsRoot};
        Check(LocalIconSources.StoreIcons(teamsOwned,64).Any(f=>f.EndsWith("TeamsForWorkNewStoreLogo.png",StringComparison.OrdinalIgnoreCase)),"Owned package manifest still resolves its real logo");
        Check(IconSourceGuard.IsOwned(teamsOwned,teamsImage)&&!IconSourceGuard.IsOwned(steamEntry,teamsImage),"Ownership guard accepts only the entry's own package asset");
        Check(!IconSourceGuard.IsOwned(steamEntry,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe")),"Guard discards an unrelated system source");
        Check(LocalIconSources.PackedProductId("{12345678-1234-5678-90AB-CDEF12345678}")=="876543214321876509BADCFE21436587","MSI packed product identifier");
        string key=ProgramIconDiskCache.SourceKey(png,0,64);File.SetLastWriteTimeUtc(png,DateTime.UtcNow.AddMinutes(-1));
        Check(key!=ProgramIconDiskCache.SourceKey(png,0,64)&&ProgramIconDiskCache.SourceKey(png,0,64)!=ProgramIconDiskCache.SourceKey(png,1,64)&&ProgramIconDiskCache.SourceKey(png,0,64)!=ProgramIconDiskCache.SourceKey(png,0,128),"Source cache key changes with timestamp/resource index/DPI size");
        var decoded=LocalIconDecoder.Load(new(png,0,IconResolutionMethod.Registry),64)!;
        Check(decoded.IsFrozen&&decoded.PixelWidth<=64,"Local PNG decoder returns frozen bounded pixels");
        var icon=new ProgramIcon(decoded,null){SourcePath=png,Method=IconResolutionMethod.Registry};ProgramIconDiskCache.Save(fixture,icon);
        Check(ProgramIconDiskCache.TryLoad(fixture)?.Image is BitmapSource {IsFrozen:true},"Disk cache restores frozen bitmap");
        File.SetLastWriteTimeUtc(png,DateTime.UtcNow);Check(ProgramIconDiskCache.TryLoad(fixture)==null,"Changed source invalidates disk cache");
        Check(IconBadgeService.Tint("Fixture")==IconBadgeService.Tint("fixture")&&IconBadgeService.Letter("Fixture")=="F"&&IconBadgeService.Letter("برنامج")=="ب","Badge stable tint and English/Arabic grapheme");
        Check(LocalIconSources.Component(new("Microsoft GameInput","",""))&&LocalIconSources.Component(new("Visual C++ Redistributable","","")),"Components get intentional neutral glyph");
        Check(LocalIconSources.GenericInstallerSource(@"C:\Windows\Installer\{B9292776-A87E-404C-8569-72CEC689F6C6}\Installer.ico",IconResolutionMethod.Msi)&&LocalIconSources.GenericInstallerSource(@"C:\ProgramData\Package Cache\x\vcredist_x64.exe",IconResolutionMethod.Legacy),"Generic installer and redistributable pictures are recognized");
        Check(!LocalIconSources.GenericInstallerSource(@"C:\Program Files\NVIDIA Corporation\Installer2\InstallerCore\NVI2.dll",IconResolutionMethod.Legacy),"A vendor logo is not treated as generic installer art");
        var runtime=new InstalledProgram("Microsoft Visual C++ 2015 Redistributable","Microsoft","14.0");
        var genericIcon=new ProgramIcon(new DrawingImage(),null){SourcePath=@"C:\Windows\Installer\{B9292776-A87E-404C-8569-72CEC689F6C6}\Installer.ico",Method=IconResolutionMethod.Msi};
        var vendorIcon=new ProgramIcon(new DrawingImage(),null){SourcePath=@"C:\Program Files\NVIDIA Corporation\Installer2\InstallerCore\NVI2.dll",Method=IconResolutionMethod.Legacy};
        Check(ComponentIconPolicy.UseNeutral(runtime,null)&&ComponentIconPolicy.UseNeutral(runtime,genericIcon),"A runtime without a real logo uses the neutral component icon");
        Check(!ComponentIconPolicy.UseNeutral(runtime,vendorIcon),"A component with a real vendor logo keeps that logo");
        Check(!ComponentIconPolicy.UseNeutral(new InstalledProgram("Google Chrome","Google","1"),null),"A non-component row keeps its letter badge");
        string broken=Path.Combine(install,"broken.ico");File.WriteAllText(broken,"broken");Check(LocalIconDecoder.Load(new(broken,0,IconResolutionMethod.Registry),64)==null,"Corrupt local icon safely rejected");
        // The known-good pre-Part-C resolver runs first; the isolated worker stays as
        // an extra fallback, so both methods are valid for these fixtures.
        var resolve=ApplicationIconResolver.ResolveAsync(fixture);Wait(resolve);Check((resolve.Result?.Method is IconResolutionMethod.Registry or IconResolutionMethod.Legacy)&&resolve.Result.Image.IsFrozen,"Actual cold pipeline resolves the fixture PNG and returns a frozen bitmap");
        var fallback=ApplicationIconResolver.ResolveAsync(fixture with{Name="Fixture fallback",IconPath=broken,RegisteredIcon=broken});Wait(fallback);Check(fallback.Result?.Method is IconResolutionMethod.Executable or IconResolutionMethod.Legacy,"Corrupt registered icon falls through to the owned executable icon");
        foreach(int pixels in new[]{32,48,56}){var dpi=ApplicationIconResolver.ResolveAsync(fixture with{Name="Fixture DPI "+pixels},pixels);Wait(dpi);Check(dpi.Result?.Image is BitmapSource bitmap&&bitmap.IsFrozen&&bitmap.PixelWidth<=pixels,"DPI-aware cold extraction at "+pixels+" device pixels");}
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();var canceled=ApplicationIconResolver.ResolveAsync(fixture,64,cancel.Token);try{Wait(canceled);}catch(OperationCanceledException){}Check(canceled.IsCanceled,"Offscreen cancellation does not publish icon");}
        var row=new PolarUninstallerWpf.ProgramIconView{Width=28,Height=28,DataContext=new PolarUninstallerWpf.ProgramCard(fixture,false)};
        var rowWindow=new System.Windows.Window{Left=-20000,Top=-20000,Width=80,Height=80,ShowInTaskbar=false,Content=row};rowWindow.Show();Pump();
        var rowImage=row.Children.OfType<System.Windows.Controls.Image>().Single();var rowClock=System.Diagnostics.Stopwatch.StartNew();
        while(rowImage.Source==null&&rowClock.Elapsed.TotalSeconds<5){Pump();Thread.Sleep(3);}
        Check(rowImage.Source is BitmapSource{IsFrozen:true},"Actual loaded row replaces its immediate badge with a frozen icon");rowWindow.Close();Pump();
        // Regression: the installed Single-File host reports an empty Assembly.Location.
        // Worker discovery must therefore use ProcessPath / BaseDirectory, including
        // an install folder whose name contains Arabic characters.
        string bin=Path.Combine(project,"bin","Release","net9.0-windows","win-x64");
        string arabicInstall=Path.Combine(root,"تثبيت عربي","Polar Uninstaller");Directory.CreateDirectory(arabicInstall);
        string arabicExe=Path.Combine(arabicInstall,"PolarUninstaller.exe");File.Copy(Path.Combine(bin,"PolarUninstaller.exe"),arabicExe,true);
        string selected=IconWorkerPool.SelectExecutable(arabicExe,"",arabicInstall,true);
        Check(selected==Path.GetFullPath(arabicExe)&&Path.IsPathFullyQualified(selected),"Single-file worker uses Environment.ProcessPath inside an Arabic install folder");
        Check(IconWorkerPool.SelectExecutable(null,"",arabicInstall,false)==Path.GetFullPath(arabicExe),"Worker falls back to an existing AppContext.BaseDirectory sibling, never a relative Assembly.Location");
        var workerStart=IconWorkerPool.BuildStartInfo(arabicExe);
        Check(workerStart.UseShellExecute==false&&workerStart.ArgumentList.Contains("--polar-icon-worker")&&workerStart.WorkingDirectory==arabicInstall,"Worker command line quotes Arabic/space paths and passes the worker argument");
        string arabicDll=Path.Combine(arabicInstall,"PolarUninstaller.dll");File.Copy(Path.Combine(bin,"PolarUninstaller.dll"),arabicDll,true);
        var managedStart=IconWorkerPool.BuildStartInfo(arabicDll);
        Check(Path.GetFileName(managedStart.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase)&&managedStart.ArgumentList.Count==2&&managedStart.ArgumentList[0]==arabicDll&&managedStart.ArgumentList[1]=="--polar-icon-worker","Managed worker keeps the Arabic DLL path as one argument");
        string? discoveryFailure=null;try{_=IconWorkerPool.SelectExecutable(@"C:\missing\dotnet.exe","",Path.Combine(root,"empty-base"),false);}catch(FileNotFoundException){discoveryFailure="missing";}
        Check(discoveryFailure=="missing","Missing ProcessPath and BaseDirectory host is a clear worker_start failure instead of a silent badge");
        // Failures are never persisted and get only a short in-process cooldown.
        var missing=fixture with{Name="Fixture missing",Location=Path.Combine(root,"no-such-install"),IconPath="",RegisteredIcon=""};
        var noIcon=ApplicationIconResolver.ResolveAsync(missing);Wait(noIcon);
        Check(noIcon.Result==null&&ProgramIconDiskCache.TryLoad(missing)==null,"Unresolvable entry returns a letter badge and is never written to the icon cache");
        string staleCache=Path.Combine(root,"stale-cache");Directory.CreateDirectory(staleCache);
        File.WriteAllText(Path.Combine(staleCache,"failure.json"),"{\"Source\":\"\"}");File.WriteAllText(Path.Combine(staleCache,"old.png"),"stale");
        ProgramIconDiskCache.UseCacheDirectory(staleCache);
        Check(!File.Exists(Path.Combine(staleCache,"failure.json"))&&!File.Exists(Path.Combine(staleCache,"old.png"))&&File.Exists(Path.Combine(staleCache,"cache-version.txt")),"IconCache version bump automatically discards old failure entries");
        var diagnosticsLog=new ActivityLogService(app.Dispatcher);
        IconDiagnostics.Begin(new[]{fixture,missing},diagnosticsLog);
        IconDiagnostics.Report(fixture,true,"legacy","");IconDiagnostics.Report(missing,false,"worker_start","native=740");IconDiagnostics.Summary();Pump();
        Check(diagnosticsLog.Entries.Any(e=>e.Severity=="INFO"&&e.Message.StartsWith("Icons: 1 resolved, 1 badges. Failures: legacy 0, worker_start 1, timeout 0, no_source 0, decode_error 0",StringComparison.Ordinal)),"Activity Log gets one categorized INFO icon summary per scan");
        Check(diagnosticsLog.Entries.Any(e=>e.Severity=="INFO"&&e.Message.Contains("Icon failed: Fixture missing; reason=worker_start")),"Each failed icon is logged once with its reason");
        // Two separate cards with identical metadata must still count as two badges.
        var duplicateLog=new ActivityLogService(app.Dispatcher);
        var duplicateA=fixture with{Name="Fixture duplicate"};var duplicateB=fixture with{Name="Fixture duplicate"};
        IconDiagnostics.Begin(new[]{duplicateA,duplicateB},duplicateLog);
        IconDiagnostics.Report(duplicateA,false,"no_source","");IconDiagnostics.Report(duplicateB,false,"no_source","");IconDiagnostics.Summary();Pump();
        Check(duplicateLog.Entries.Any(e=>e.Severity=="INFO"&&e.Message.StartsWith("Icons: 0 resolved, 2 badges.",StringComparison.Ordinal)),"Icon summary counts duplicate cards separately, matching the visible list");
        ProgramIconDiskCache.UseCacheDirectory(Path.Combine(root,"icons"));
        // Read-only inventory audit. Only fixture-local cache and output writes; no cleanup or uninstaller runs.
        var inventory=new ApplicationInventoryService().RefreshAsync();Wait(inventory);var programs=inventory.Result.Programs;
        var results=new System.Collections.Concurrent.ConcurrentBag<object>();
        var audit=Task.Run(async()=>await Task.WhenAll(programs.Select(async p=>
        {
            var result=await ApplicationIconResolver.ResolveAsync(p);
            results.Add(new {p.Name,p.Source,Method=(result?.Method??(LocalIconSources.Component(p)?IconResolutionMethod.Component:IconResolutionMethod.Badge)).ToString(),IconSource=result?.SourcePath??""});
        })));
        Wait(audit);File.WriteAllText(Path.Combine(root,"machine-icons.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        var counts=results.Select(x=>JsonSerializer.SerializeToElement(x)).GroupBy(x=>x.GetProperty("Method").GetString()).ToDictionary(g=>g.Key!,g=>g.Count());
        Console.WriteLine("READ-ONLY MACHINE ICON COUNTS "+JsonSerializer.Serialize(counts));
        Check(results.Count==programs.Count,"Read-only machine inventory audit resolves every entry or records explicit fallback");
        IconWorkerPool.StopAll();return Finish("C");
    }
}

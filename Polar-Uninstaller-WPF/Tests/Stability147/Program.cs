using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using PolarUninstallerWpf.Services;
internal static class Program
{
    [DllImport("user32.dll")]static extern int GetGuiResources(IntPtr process,int kind);
    static async Task Main(string[] args)
    {
        if(args[0]=="--never-exit"){Console.WriteLine("READY");await Task.Delay(Timeout.InfiniteTimeSpan);return;}
        string root=Path.GetFullPath(args[0]),label=args[1];Directory.CreateDirectory(root);
        string tree=Path.Combine(root,"مجلد عربي طويل للاختبار");Directory.CreateDirectory(tree);
        if(!File.Exists(Path.Combine(root,"fixture-ready")))
        {
            for(int d=0;d<100;d++){string folder=Path.Combine(tree,"مجلد-"+d);Directory.CreateDirectory(folder);for(int i=0;i<1000;i++){using var created=File.Create(Path.Combine(folder,"عنصر-"+i+".tmp"));}}
            File.WriteAllText(Path.Combine(root,"fixture-ready"),"100000 owned zero-length fixtures");
        }
        var output=new Dictionary<string,object?>();var clock=Stopwatch.StartNew();
        string settings=Path.Combine(root,"settings-"+label);Directory.CreateDirectory(settings);File.WriteAllText(Path.Combine(settings,"settings.json"),"{broken");File.WriteAllText(Path.Combine(settings,"settings.json.bak"),JsonSerializer.Serialize(new{Theme="Blue"}));
        var loaded=new SettingsService(settings).Load();output["CorruptSettingsTheme"]=loaded.Theme;output["SettingsLoadMs"]=clock.Elapsed.TotalMilliseconds;
        string metrics=Path.Combine(root,"metrics-"+label);Directory.CreateDirectory(metrics);File.WriteAllText(Path.Combine(metrics,"activity-metrics.json"),JsonSerializer.Serialize(new{Opens=1,Clicks=2,Days=(object?)null}));
        try{new ActivityMetricsService(metrics).Record(false);output["NullMetricsRecovered"]=true;}catch(Exception e){output["NullMetricsRecovered"]=false;output["NullMetricsError"]=e.GetType().Name;}
        DashboardStorageService.UseCacheDirectory(Path.Combine(root,"cache-"+label));clock.Restart();var scan=await new DashboardStorageService().ScanAsync(tree,default);output["Scan100000Ms"]=clock.Elapsed.TotalMilliseconds;output["Files"]=scan.FilesScanned;
        string tiny=Path.Combine(root,"tiny");Directory.CreateDirectory(tiny);for(int i=0;i<10;i++)File.WriteAllText(Path.Combine(tiny,i+".txt"),"fixture");
        var browser=new FileBrowserService();await browser.ListAsync(tiny,default);
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();using var process=Process.GetCurrentProcess();process.Refresh();
        long memory=GC.GetTotalMemory(true);int handles=process.HandleCount,gdi=GetGuiResources(process.Handle,0),user=GetGuiResources(process.Handle,1);
        var timings=new List<double>();var history=new FolderNavigationHistory(tiny);
        for(int i=0;i<500;i++){clock.Restart();await browser.ListAsync(tiny,default);history.Commit(tree);history.Commit(tiny);timings.Add(clock.Elapsed.TotalMilliseconds);}
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();process.Refresh();
        output["Cycles"]=500;output["NavigationP95Ms"]=timings.Order().ElementAt(474);output["NavigationMaxMs"]=timings.Max();output["ManagedMemoryDeltaBytes"]=GC.GetTotalMemory(true)-memory;output["HandleDelta"]=process.HandleCount-handles;output["GdiDelta"]=GetGuiResources(process.Handle,0)-gdi;output["UserDelta"]=GetGuiResources(process.Handle,1)-user;
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();try{await new DashboardStorageService().ScanAsync(tree,cancelled.Token);output["Cancellation"]=false;}catch(OperationCanceledException){output["Cancellation"]=true;}
        string file=Path.Combine(tiny,"locked.json");File.WriteAllText(file,"old");using(var locked=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){try{File.ReadAllText(file);output["LockedFixture"]=false;}catch(IOException){output["LockedFixture"]=true;}}
        if(label=="after")AfterFaults(root,output);
        File.WriteAllText(Path.Combine(root,label+".json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(output));if(label=="after"&&(output.Values.OfType<bool>().Any(v=>!v)||scan.FilesScanned!=100000||output["ManagedMemoryDeltaBytes"] is long growth&&growth>32L*1024*1024))Environment.ExitCode=1;
    }
    private static void AfterFaults(string root,Dictionary<string,object?> output)
    {
        string longRoot=Path.Combine(root,"مسار طويل");for(int i=0;i<16;i++)longRoot=Path.Combine(longRoot,"مجلد_تجريبي_باسم_عربي_طويل_"+i);Directory.CreateDirectory(longRoot);File.WriteAllText(Path.Combine(longRoot,"ملف.txt"),"owned fixture");output["LongArabicPathListed"]=new FileBrowserService().ListAsync(longRoot,default).GetAwaiter().GetResult().Count==1;
        var childStart=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};childStart.ArgumentList.Add(typeof(Program).Assembly.Location);childStart.ArgumentList.Add("--never-exit");
        using(var child=Process.Start(childStart)!)try{child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();try{child.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(100)).GetAwaiter().GetResult();}catch(TimeoutException){child.Kill(true);child.WaitForExit(5000);}output["NeverExitChildCleaned"]=child.HasExited;}finally{if(!child.HasExited)child.Kill(true);}
        string logs=Path.Combine(root,"owned-diagnostics");AppSafety.LogDirectory=logs;CrashDiagnostics.LogDirectory=logs;
        output["UacCancelledRecoverable"]=AppSafety.IsRecoverable(new System.ComponentModel.Win32Exception(1223));
        output["ElevationRequiredRecoverable"]=AppSafety.IsRecoverable(new System.ComponentModel.Win32Exception(740));
        output["RecoverableShellComError"]=AppSafety.IsRecoverable(new System.Runtime.InteropServices.COMException("fixture",unchecked((int)0x80004005)));
        output["AccessDeniedRecoverable"]=AppSafety.IsRecoverable(new UnauthorizedAccessException("fixture"));
        output["ProgrammingFaultNotSwallowed"]=!AppSafety.IsRecoverable(new NullReferenceException("fixture"));
        for(int i=0;i<15;i++)AppSafety.WriteCrash("fixture",new IOException("owned failure "+i));
        output["CrashRotation"]=Directory.GetFiles(logs,"crash-*.txt").Length==10;
        string state=Path.Combine(root,"atomic-state.json");File.WriteAllText(state,"old");
        try{AtomicStateFile.Write(state,"new",true,(_,_)=>throw new IOException("fake disk full"));}catch(IOException){}
        output["DiskFullKeepsOldState"]=File.ReadAllText(state)=="old";
        AtomicStateFile.Write(state,"new");output["AtomicBackup"]=File.ReadAllText(state+".bak")=="old"&&File.ReadAllText(state)=="new";
        string cache=Path.Combine(root,"corrupt-cache");Directory.CreateDirectory(cache);File.WriteAllText(Path.Combine(cache,"broken.json"),"{broken");DashboardStorageService.UseCacheDirectory(cache);DashboardStorageService.LoadCacheAsync().GetAwaiter().GetResult();output["CorruptCacheTolerated"]=true;
        var palette=new System.Windows.ResourceDictionary();for(int i=0;i<200;i++)CalmThemeBrushes.Apply(palette,ThemeCatalog.All.Values.ElementAt(i%8));output["ThemeSwitches"]=200;output["BoundedThemeResources"]=palette.Count<32;
        var never=new TaskCompletionSource();try{never.Task.WaitAsync(TimeSpan.FromMilliseconds(20)).GetAwaiter().GetResult();output["HungWorkTimeout"]=false;}catch(TimeoutException){output["HungWorkTimeout"]=true;}
        string host=Path.Combine(root,"تشغيل عربي.exe");File.WriteAllText(host,"fixture; never executed");output["WorkerHostUsesActualExe"]=IconWorkerPool.SelectExecutable(host,Path.Combine(root,"extracted","PolarUninstaller.dll"),root,true)==host;
    }
}

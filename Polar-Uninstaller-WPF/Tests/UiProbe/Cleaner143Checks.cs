using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using PolarUninstallerWpf.Services;

internal static class Cleaner143Checks
{
    internal static int Run()
    {
        int failures=0,count=0;
        void Check(bool pass,string label){count++;if(!pass)failures++;Console.WriteLine((pass?"PASS ":"FAIL ")+label);}
        string project=AppContext.BaseDirectory;
        while(!File.Exists(Path.Combine(project,"PolarUninstallerWpf.csproj")))project=Directory.GetParent(project)?.FullName??throw new IOException("Project root missing");
        string root=Path.Combine(project,"Build","Validation","Cleaner143-"+Guid.NewGuid().ToString("N"),"temp");
        string runtime=Path.Combine(root,"active-bundle"),updates=Path.Combine(root,"PolarWpfUpdate-fixture");
        Directory.CreateDirectory(runtime);Directory.CreateDirectory(updates);
        string asset=Path.Combine(runtime,"required-runtime.dll"),setup=Path.Combine(updates,"setup.exe"),trash=Path.Combine(root,"disposable.tmp");
        File.WriteAllText(asset,"synthetic runtime asset; never executed");File.WriteAllText(setup,"synthetic setup; never executed");File.WriteAllText(trash,"disposable");
        string? previous=Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR",runtime);
            var protectedPaths=CleanerRuntimeProtection.Directories();
            Check(CleanerRuntimeProtection.IsProtected(Path.Combine(Path.GetTempPath(),".net","PolarUninstaller","bundle","runtime.dll"),protectedPaths),"Default single-file runtime extraction cache is protected");
            Check(CleanerRuntimeProtection.IsProtected(asset,protectedPaths),"Custom extraction directory is protected");
            Check(!CleanerRuntimeProtection.IsProtected(runtime+"-other",protectedPaths),"Protection uses directory boundaries rather than broad name matching");
            Check(CleanerRuntimeProtection.IsProtected(updates,protectedPaths),"Pending Polar updater directory is protected");
            var method=typeof(TempCleanerService).GetMethod("RunInDirectoryAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var service=new TempCleanerService();
            TempCleanerService.Result Run(bool delete)=>((Task<TempCleanerService.Result>)method.Invoke(service,new object?[]{root,delete,null,CancellationToken.None,false})!).GetAwaiter().GetResult();
            var scan=Run(false);
            Check(scan.Files==1&&scan.Skipped==2,"Scan excludes runtime and update payloads from removable totals");
            var clean=Run(true);
            Check(clean.Files==1&&!File.Exists(trash),"Cleaner removes only the disposable eligible temporary file");
            Check(File.Exists(asset)&&File.Exists(setup),"Synthetic runtime and updater files survive real scoped cleanup");
        }
        finally{Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR",previous);}
        var nativeLogic=typeof(RecycleBinCleaner).GetMethod("Run",BindingFlags.Static|BindingFlags.NonPublic)!;
        TempCleanerService.Result Recycle(Func<(long Bytes,long Items)> query,Func<int> empty,bool delete=true)=>
            (TempCleanerService.Result)nativeLogic.Invoke(null,new object[]{query,empty,delete,CancellationToken.None})!;
        int calls=0;
        var empty=Recycle(()=>(0L,0L),()=>{calls++;return unchecked((int)0x8000FFFF);});
        Check(calls==0&&empty.Files==0,"Already-empty Recycle Bin does not call native emptying API");
        var readOnly=Recycle(()=>(100L,2L),()=>throw new Exception("must not empty during scan"),false);
        Check(readOnly.Files==2&&readOnly.Bytes==100,"Scanning the bin is read-only");
        var samples=new Queue<(long Bytes,long Items)>(new[]{(100L,2L),(40L,1L)});
        var partial=Recycle(()=>samples.Dequeue(),()=>unchecked((int)0x80004005));
        Check(partial.Files==1&&partial.Bytes==60&&partial.Skipped==1,"Partial native removal is verified and remaining items reported");
        bool rejected=false;
        try{Recycle(()=>(100L,2L),()=>unchecked((int)0x80004005));}
        catch(TargetInvocationException ex) when(ex.InnerException is COMException){rejected=true;}
        Check(rejected,"Unchanged bin with native error is not reported as successful");
        string ar=DisplayFormat.CleanOutcomeSummary(1024,1,0,0,1,true);
        string en=DisplayFormat.CleanOutcomeSummary(1024,1,0,0,1,false);
        Check(!ar.Contains("بنجاح")&&ar.Contains("جزئيًا")&&en.Contains("partially"),"Failed categories produce partial completion in Arabic and English");
        Console.WriteLine($"{count} checks, {failures} failures. Only owned fixture files were removed; the real Recycle Bin was never emptied.");
        return failures==0?0:1;
    }
}

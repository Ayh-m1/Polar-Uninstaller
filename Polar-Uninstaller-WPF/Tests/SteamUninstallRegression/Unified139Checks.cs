using PolarUninstallerWpf.Services;

internal static class Unified139Checks
{
    internal static async Task<int> RunAsync()
    {
        string root=Path.Combine(AppContext.BaseDirectory,"fixtures","unified139-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);int passed=0;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
        try
        {
            string install=Path.Combine(root,"SyntheticApp");Directory.CreateDirectory(install);
            string main=Path.Combine(install,"SyntheticApp.exe"),temporary=Path.Combine(install,"ordinary.tmp"),locked=Path.Combine(install,"locked.tmp");
            string settings=Path.Combine(install,"settings.json"),profile=Path.Combine(install,"profiles","user.sav");
            Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
            File.WriteAllText(main,"disposable executable-shaped data, never executed");
            File.WriteAllText(temporary,"eligible ordinary temporary fixture");File.WriteAllText(locked,"eligible locked temporary fixture");
            File.WriteAllText(settings,"personal settings fixture");File.WriteAllText(profile,"personal save fixture");
            var app=new InstalledProgram("Synthetic Unified Fixture "+Guid.NewGuid().ToString("N"),"Synthetic Fixture Publisher","1.0",install);
            var snapshot=new UninstallSnapshot(app,[],[],[],[main]);
            var engine=new RemovalEngine();
            var plan=await engine.PlanAsync(snapshot,[],false,default);
            Check(plan.Files.Any(f=>f.Path==temporary)&&plan.Files.Any(f=>f.Path==locked),"Original verified cleanup scope captures routine temporary files");
            Check(!plan.Files.Any(f=>f.Path==settings||f.Path==profile),"Settings and save data are excluded from routine file authorization");
            Check(!plan.Directories.Contains(Path.GetDirectoryName(profile)!),"Personal profile subtree is not traversed into the deletion plan");
            Check(RemovalDataPolicy.Preserve(settings,install)&&RemovalDataPolicy.Preserve(profile,install),"Central data policy protects settings and profiles");
            Check(RemovalDataPolicy.Preserve(Path.Combine(root,"unrelated.tmp"),install),"Data policy rejects paths outside the verified installation scope");
            File.Delete(main); // Simulate ONLY this disposable app's official removal.
            var initialProbe=UninstallMonitorService.Probe(app,snapshot);
            Check(!UninstallVerificationService.AnyExecutablePresent(snapshot.Executables),"Captured disposable executable was removed; personal data remains");
            if(!initialProbe.Confirmed)Console.WriteLine("LIMITATION Real Registry verification is unavailable or uncertain here: "+initialProbe.Detail+" Cleanup test uses a synthetic confirmed official stage.");
            RemovalPass first;
            using(var held=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.None))
            {
                first=await engine.ExecuteAsync(plan,[],null,default);
                Check(!File.Exists(temporary),"Confirmed official removal can automatically clean eligible routine leftovers");
                Check(File.Exists(locked)&&first.Cleanup.Failed>0,"Locked eligible resource is retained and reported for exceptional recovery");
                Check(first.Cleanup.RemainingPaths.Contains(locked),"Recovery retains the original failed-resource scope");
            }
            var retry=await engine.ExecuteAsync(plan,[],null,default);
            Check(!File.Exists(locked)&&retry.Cleanup.Failed==0,"Existing engine can retry the preserved scope after the lock is released");
            Check(File.Exists(settings)&&File.ReadAllText(settings)=="personal settings fixture"&&File.ReadAllText(profile)=="personal save fixture","Automatic cleanup and retry preserve personal contents exactly");
            Check(retry.Remaining.Count==0&&retry.Cleanup.RemainingPaths.Count==0,"Retained personal data does not force a routine Advanced stage");
            var final=UninstallReportService.FromProbe(app,new RemovalProbe(true,false,true,"Synthetic confirmed official stage; Registry verification not exercised",false),retry.Cleanup,"disposable fixture");
            Check(final.Outcome==UninstallOutcome.SuccessfullyRemoved,"Final verification distinguishes complete eligible cleanup from retained personal data");
            Check(snapshot.Program==app&&plan.Snapshot==snapshot,"The same pre-uninstall identity survives removal and cleanup");
            Console.WriteLine(passed+" focused unified-cleanup checks passed; only disposable fixture files were removed.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine("FAIL "+e);return 1;}
        finally{Directory.Delete(root,true);}
    }
}

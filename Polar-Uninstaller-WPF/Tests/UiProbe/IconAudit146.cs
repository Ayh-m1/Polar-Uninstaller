using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using PolarUninstallerWpf.Services;

/// <summary>
/// Read-only machine audit: resolves every installed entry through the pre-Part-C
/// resolver (baseline) and through the production resolver (after), and records the
/// exact source file so a wrong logo can be traced. It never launches an uninstaller,
/// deletes a file or changes a real icon.
/// </summary>
internal static class IconAudit146
{
    internal static int Run()
    {
        var app=new Application();SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        void Pump(){var frame=new DispatcherFrame();app.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
        void Wait(Task task){var watch=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted&&watch.Elapsed<TimeSpan.FromSeconds(120))Pump();if(!task.IsCompleted)throw new TimeoutException("Icon audit timed out");task.GetAwaiter().GetResult();}
        string project=AppContext.BaseDirectory;
        while(!File.Exists(Path.Combine(project,"PolarUninstallerWpf.csproj")))project=Directory.GetParent(project)?.FullName??throw new IOException("Project root missing");
        string root=Path.Combine(project,"Build","Validation","IconAudit146-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        ProgramIconDiskCache.UseCacheDirectory(Path.Combine(root,"icons"));
        string workerHost=Environment.GetEnvironmentVariable("POLAR_ICON_AUDIT_HOST") is {Length:>0} host
            ? Path.GetFullPath(host)
            : Path.Combine(project,"bin","Release","net9.0-windows","win-x64","PolarUninstaller.dll");
        Console.WriteLine($"AUDIT worker host={workerHost}");
        IconWorkerPool.ConfigureExecutable(workerHost);
        var inventory=new ApplicationInventoryService().RefreshAsync();Wait(inventory);var programs=inventory.Result.Programs;
        var before=new Dictionary<string,(string Icon,string Method,string Source,string Index)>(StringComparer.Ordinal);
        var after=new Dictionary<string,(string Icon,string Method,string Source,string Index)>(StringComparer.Ordinal);
        string Key(InstalledProgram p)=>p.Name+"|"+p.Location;
        foreach(var program in programs)
        {
            var legacy=LegacyApplicationIconResolver.ResolveAsync(program);Wait(legacy);
            var icon=legacy.Result;
            before[Key(program)]=icon==null?("badge","","",""):("icon",icon.Method.ToString(),icon.SourcePath,icon.SourceIndex.ToString());
        }
        foreach(var program in programs)
        {
            var resolved=ApplicationIconResolver.ResolveAsync(program);Wait(resolved);
            var icon=resolved.Result;
            after[Key(program)]=icon==null?("badge","","",""):("icon",icon.Method.ToString(),icon.SourcePath,icon.SourceIndex.ToString());
        }
        var rows=programs.Select(p=>new
        {
            p.Name,p.Location,p.Identifier,p.Source,p.Version,SizeKb=p.SizeKb,p.Publisher,
            p.Sources,p.RegisteredIcon,p.IconCandidates,
            BeforeIcon=before[Key(p)].Icon,
            BeforeMethod=before[Key(p)].Method,
            BeforeSource=before[Key(p)].Source,
            AfterIcon=after[Key(p)].Icon,
            AfterMethod=after[Key(p)].Method,
            AfterSource=after[Key(p)].Source,
            AfterIndex=after[Key(p)].Index,
            BeforeOwned=before[Key(p)].Icon=="icon"&&IconSourceGuard.IsOwned(p,before[Key(p)].Source),
            SourceOwned=after[Key(p)].Icon=="icon"&&IconSourceGuard.IsOwned(p,after[Key(p)].Source)
        }).OrderBy(r=>r.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
        File.WriteAllText(Path.Combine(root,"icon-audit.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        int beforeIcons=rows.Count(r=>r.BeforeIcon!="badge"),afterIcons=rows.Count(r=>r.AfterIcon!="badge");
        int lost=rows.Count(r=>r.BeforeIcon!="badge"&&r.AfterIcon=="badge");
        int unowned=rows.Count(r=>r.AfterIcon!="badge"&&!r.SourceOwned);
        int unownedBefore=rows.Count(r=>r.BeforeIcon!="badge"&&!r.BeforeOwned);
        Console.WriteLine($"AUDIT entries={rows.Length} beforeReal={beforeIcons} beforeBadge={rows.Length-beforeIcons} afterReal={afterIcons} afterBadge={rows.Length-afterIcons} lost={lost} unowned={unowned} unownedBefore={unownedBefore}");
        foreach(var row in rows)Console.WriteLine($"{(row.AfterIcon=="badge"?"BADGE":"ICON ")} | before={row.BeforeIcon,-8} | after={row.AfterMethod,-12} | owned={row.SourceOwned,-5} | {row.Name} | {(row.AfterSource.Length>0?row.AfterSource:row.AfterMethod)}");
        foreach(var row in rows.Where(r=>r.AfterIcon!="badge"&&!r.SourceOwned))
            Console.WriteLine($"UNOWNED | {row.Name} | {row.AfterMethod} | {row.AfterSource} | location={row.Location}");
        IconWorkerPool.StopAll();
        Console.WriteLine("Audit written to "+Path.Combine(root,"icon-audit.json"));
        return lost==0?0:1;
    }
}

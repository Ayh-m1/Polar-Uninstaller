using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
public static class StartupHook
{
    public static void Initialize()
    {
        string? output=Environment.GetEnvironmentVariable("POLAR_ICON_PROBE_OUTPUT");if(output==null)return;
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS",null);
        _=Task.Run(async()=>
        {
            var report=new Dictionary<string,object?>();
            try
            {
                Assembly? app=null;for(int i=0;i<100;i++){app=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="PolarUninstaller");if(app!=null)break;await Task.Delay(50);}if(app==null)throw new Exception("Application assembly did not load");
                app.GetType("PolarUninstallerWpf.Services.ProgramIconDiskCache")!.GetMethod("UseCacheDirectory")!.Invoke(null,new object[]{Path.Combine(Path.GetDirectoryName(output)!,"cache-"+Path.GetFileNameWithoutExtension(output))});
                report["ProcessPath"]=Environment.ProcessPath;report["AssemblyLocation"]=app.Location;
                string oldHost=string.IsNullOrEmpty(app.Location)?Environment.ProcessPath!:Path.Combine(Path.GetDirectoryName(app.Location)!,"PolarUninstaller.exe");report["LegacyWorkerPath"]=oldHost;report["LegacyWorkerExists"]=File.Exists(oldHost);
                if(!File.Exists(oldHost))try{using var missing=Process.Start(new ProcessStartInfo(oldHost){UseShellExecute=false,CreateNoWindow=true});}catch(System.ComponentModel.Win32Exception e){report["LegacyStartErrorCode"]=e.NativeErrorCode;report["LegacyStartError"]=e.Message;}
                var inventory=app.GetType("PolarUninstallerWpf.Services.ApplicationInventoryService")!;var service=Activator.CreateInstance(inventory)!;
                var scan=(Task)inventory.GetMethod("RefreshAsync")!.Invoke(service,new object[]{CancellationToken.None})!;await scan;
                var snapshot=scan.GetType().GetProperty("Result")!.GetValue(scan)!;
                var programs=(System.Collections.IEnumerable)snapshot.GetType().GetProperty("Programs")!.GetValue(snapshot)!;
                // Capture the exact Activity Log lines the production scan writes.
                var log=new List<string>();
                var diagnostics=app.GetType("PolarUninstallerWpf.Services.IconDiagnostics")!;
                diagnostics.GetEvent("Written")!.AddEventHandler(null,new Action<string>(line=>{lock(log)log.Add(line);}));
                diagnostics.GetMethod("Begin")!.Invoke(null,new object?[]{programs,null});
                var resolver=app.GetType("PolarUninstallerWpf.Services.ApplicationIconResolver")!.GetMethod("ResolveAsync")!;
                var legacy=app.GetType("PolarUninstallerWpf.Services.LegacyApplicationIconResolver")?.GetMethod("ResolveAsync");
                var rows=new List<object>();var losses=new List<string>();
                foreach(var p in programs)
                {
                    var type=p!.GetType();string name=(string)type.GetProperty("Name")!.GetValue(p)!;
                    try{object? oldIcon=null;if(legacy!=null){var oldTask=(Task)legacy.Invoke(null,new[]{p})!;await oldTask;oldIcon=oldTask.GetType().GetProperty("Result")!.GetValue(oldTask);}
                    var task=(Task)resolver.Invoke(null,new[]{p,(object)64,CancellationToken.None,true})!;await task;var icon=task.GetType().GetProperty("Result")!.GetValue(task);if(oldIcon!=null&&icon==null)losses.Add(name);rows.Add(new{Name=name,OldResolved=oldIcon!=null,OldSource=oldIcon?.GetType().GetProperty("SourcePath")?.GetValue(oldIcon)?.ToString(),Resolved=icon!=null,Method=icon?.GetType().GetProperty("Method")?.GetValue(icon)?.ToString()??"Badge"});}
                    catch(Exception e){rows.Add(new{Name=name,Resolved=false,Method="Exception",Error=e.ToString()});}
                }
                report["AllOldIconsRetained"]=losses.Count==0;
                report["Regressions"]=losses;report["Rows"]=rows;report["Count"]=rows.Count;
                diagnostics.GetMethod("Summary")!.Invoke(null,null);
                lock(log)report["ActivityLog"]=log.ToArray();
                app.GetType("PolarUninstallerWpf.Services.IconWorkerPool")!.GetMethod("StopAll")!.Invoke(null,null);
            }
            catch(Exception error){report["Error"]=error.ToString();}
            File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        });
    }
}

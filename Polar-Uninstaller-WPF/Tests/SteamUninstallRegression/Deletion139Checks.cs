using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PolarUninstallerWpf.Services;

internal static class Deletion139Checks
{
    private static Task<T> Sta<T>(Func<T> action)
    {
        var done=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{try{done.SetResult(action());}catch(Exception e){done.SetException(e);}}){IsBackground=true};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return done.Task;
    }
    // Only matches this run's exact GUID-bearing filename AND original folder.
    // Never empties the bin, deletes bin items or operates on an unrelated entry.
    private static bool BinItem(string path,bool restore)
    {
        object? shell=null,bin=null,items=null;
        try
        {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application",true)!);
            bin=((dynamic)shell!).NameSpace(10);if(bin==null)throw new IOException("Recycle Bin namespace unavailable.");
            // Recycle namespace filtering can apply to the backing $R name,
            // not the original display name. Match original identity explicitly.
            items=((dynamic)bin).Items();
            for(int i=0;i<(int)((dynamic)items).Count;i++)
            {
                object item=((dynamic)items).Item(i);
                try
                {
                    string name=Convert.ToString(((dynamic)item).Name)??"";
                    if(name!=Path.GetFileName(path)&&name!=Path.GetFileNameWithoutExtension(path))continue;
                    string origin=Convert.ToString(((dynamic)item).ExtendedProperty("System.Recycle.DeletedFrom"))??"";
                    if(origin.Length==0)origin=Convert.ToString(((dynamic)bin).GetDetailsOf(item,1))??"";
                    if((name==Path.GetFileName(path)||name==Path.GetFileNameWithoutExtension(path))&&LeftoverService.EqualPath(origin,Path.GetDirectoryName(path)!))
                    {if(restore)((dynamic)item).InvokeVerb("undelete");return true;}
                }
                finally{Marshal.FinalReleaseComObject(item);}
            }
            return false;
        }
        finally
        {
            if(items!=null)Marshal.FinalReleaseComObject(items);if(bin!=null)Marshal.FinalReleaseComObject(bin);if(shell!=null)Marshal.FinalReleaseComObject(shell);
        }
    }
    internal static async Task<int> RunAsync()
    {
        string guid=Guid.NewGuid().ToString("N"),root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"fixtures","deletion139-"+guid));
        Directory.CreateDirectory(root);int passed=0;string recycle=Path.Combine(root,"Polar-recycle-verification-"+guid+".bin"),recycleFolder=Path.Combine(root,"Polar-recycle-folder-"+guid);bool binAvailable=false;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
        try
        {
            var ops=new BrowserOperationsService();string permanent=Path.Combine(root,"Polar-permanent-verification-"+guid+".bin");
            try{await Sta(()=>BinItem(permanent,false));binAvailable=true;}catch(Exception e){Console.WriteLine("LIMITATION Recycle Bin inspection unavailable: "+e.GetType().Name);}
            File.WriteAllText(permanent,"disposable selected original");
            var removed=await ops.DeleteAsync([permanent],true,default);
            Check(removed.Single().Success&&!File.Exists(permanent),"Permanent deletion removes the actual selected fixture");
            string permanentFolder=Path.Combine(root,"permanent-folder-"+guid);Directory.CreateDirectory(Path.Combine(permanentFolder,"nested"));File.WriteAllText(Path.Combine(permanentFolder,"nested","original.bin"),"owned nested fixture");
            Check((await ops.DeleteAsync([permanentFolder],true,default)).Single().Success&&!Directory.Exists(permanentFolder),"Permanent folder deletion removes only its selected subtree");
            if(binAvailable)Check(!await Sta(()=>BinItem(permanent,false)),"Permanent deletion creates no matching Recycle Bin entry");
            string locked=Path.Combine(root,"locked.bin");File.WriteAllText(locked,"locked fixture");
            using(var held=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.None))
            {
                var failed=await ops.DeleteAsync([locked],true,default);
                Check(!failed.Single().Success&&File.Exists(locked),"Locked permanent-delete failure leaves the original and reports failure");
            }
            string install=Path.Combine(root,"installed"),other=Path.Combine(root,"other"),shortcuts=Path.Combine(root,"shortcuts");
            Directory.CreateDirectory(install);Directory.CreateDirectory(other);Directory.CreateDirectory(shortcuts);
            string exe=Path.Combine(install,"app.exe"),otherExe=Path.Combine(other,"app.exe");File.WriteAllText(exe,"fixture, never executed");File.WriteAllText(otherExe,"fixture, never executed");
            string own=Path.Combine(shortcuts,"arbitrary-name.url"),unrelated=Path.Combine(shortcuts,"App Name.url"),document=Path.Combine(shortcuts,"notes.url");
            File.WriteAllText(own,"[InternetShortcut]\r\nURL="+new Uri(exe).AbsoluteUri);File.WriteAllText(unrelated,"[InternetShortcut]\r\nURL="+new Uri(otherExe).AbsoluteUri);
            File.WriteAllText(document,"[InternetShortcut]\r\nURL="+new Uri(Path.Combine(install,"notes.txt")).AbsoluteUri);
            var app=new InstalledProgram("Synthetic App","Synthetic","1",install);
            var links=await ShortcutLeftoverService.ScanAsync(app,locations:[shortcuts]);
            Check(links.Count==1&&links[0].Path==own,"Shortcut target evidence selects own URL and protects unrelated/document links");
            bool lnkAvailable=false;string lnk=Path.Combine(shortcuts,"Own-LNK-"+guid+".lnk");
            try{await Sta(()=>{ShellShortcutService.Write(lnk,exe);return true;});lnkAvailable=true;}
            catch(Exception e){Console.WriteLine("LIMITATION Shell-link fixture unavailable: "+e.GetType().Name);}
            if(lnkAvailable)
            {
                links=await ShortcutLeftoverService.ScanAsync(app,locations:[shortcuts]);var native=links.Single(l=>l.Path==lnk);
                Check(ShortcutLeftoverService.Remove(native,app).StartsWith("Removed shortcut permanently:")&&!File.Exists(lnk),"Verified LNK cleanup permanently removes only the shortcut");
                if(binAvailable)Check(!await Sta(()=>BinItem(lnk,false)),"Permanent LNK cleanup creates no matching bin entry");
            }
            var candidate=(await ShortcutLeftoverService.ScanAsync(app,locations:[shortcuts])).Single(l=>l.Path==own);
            Check(ShortcutLeftoverService.Remove(candidate,app).StartsWith("Removed shortcut permanently:")&&!File.Exists(own),"Verified URL shortcut is permanently removed");
            Check(File.Exists(exe)&&File.Exists(otherExe)&&File.Exists(unrelated)&&File.Exists(document),"Shortcut cleanup preserves targets, unrelated shortcuts and user documents");
            File.WriteAllText(own,"[InternetShortcut]\r\nURL="+new Uri(exe).AbsoluteUri);
            candidate=(await ShortcutLeftoverService.ScanAsync(app,locations:[shortcuts])).Single(l=>l.Path==own);
            File.WriteAllText(own,"[InternetShortcut]\r\nURL="+new Uri(otherExe).AbsoluteUri);
            Check(ShortcutLeftoverService.Remove(candidate,app).StartsWith("Skipped")&&File.Exists(own),"Retargeted shortcut fingerprint is revalidated and preserved");
            var game=app with{Identifier="steam:123",Source="Steam"};
            Check(ShortcutLeftoverService.Owns(game,"steam://rungameid/123")&&!ShortcutLeftoverService.Owns(game,"steam://rungameid/1234"),"Exact game IDs protect shortcuts for other games");
            Check(!ShortcutLeftoverService.Owns(game,Path.Combine(other,"steam.exe")),"Shared launcher shortcut without game identity is protected");
            if(binAvailable)
            {
                byte[] payload=System.Text.Encoding.UTF8.GetBytes("Original Unicode fixture بيانات "+guid);File.WriteAllBytes(recycle,payload);
                var recycled=await ops.DeleteAsync([recycle],false,default);
                Check(recycled.Single().Success&&!File.Exists(recycle),"Shell recycles the actual selected file, with a verified recycled destination: "+recycled.Single().Error+"; original remains="+File.Exists(recycle)+"; own bin item="+await Sta(()=>BinItem(recycle,false)));
                Check(await Sta(()=>BinItem(recycle,false)),"Bin contains the exact original filename and original directory");
                Check(await Sta(()=>BinItem(recycle,true)),"Normal Shell restore requested for the exact owned fixture only");
                for(int i=0;i<30&&!File.Exists(recycle);i++)await Task.Delay(100);
                Check(File.Exists(recycle)&&SHA256.HashData(File.ReadAllBytes(recycle)).SequenceEqual(SHA256.HashData(payload)),"Restored original content is unchanged");
                Directory.CreateDirectory(Path.Combine(recycleFolder,"nested"));File.WriteAllBytes(Path.Combine(recycleFolder,"nested","original.bin"),payload);
                var folderResult=(await ops.DeleteAsync([recycleFolder],false,default)).Single();
                Check(folderResult.Success&&!Directory.Exists(recycleFolder),"Selected folder receives its own verified recycled destination");
                Check(await Sta(()=>BinItem(recycleFolder,true)),"Normal Shell restore targets only the owned recycled folder");
                for(int i=0;i<30&&!File.Exists(Path.Combine(recycleFolder,"nested","original.bin"));i++)await Task.Delay(100);
                Check(SHA256.HashData(File.ReadAllBytes(Path.Combine(recycleFolder,"nested","original.bin"))).SequenceEqual(SHA256.HashData(payload)),"Recycled folder subtree restores with original content");
            }
            Check(!Directory.GetFiles(root,"recycle.txt",SearchOption.AllDirectories).Any(),"No placeholder recycle.txt is created");
            Console.WriteLine(passed+" focused deletion/shortcut checks passed; no real user targets were removed.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine("FAIL "+e);return 1;}
        finally
        {
            if(binAvailable)try{if(await Sta(()=>BinItem(recycle,false))){await Sta(()=>BinItem(recycle,true));for(int i=0;i<30&&!File.Exists(recycle);i++)await Task.Delay(100);}}catch(Exception e){Console.WriteLine("LIMITATION Owned recycle fixture may require manual restore: "+recycle+" ("+e.GetType().Name+")");}
            if(binAvailable)try{if(await Sta(()=>BinItem(recycleFolder,false))){await Sta(()=>BinItem(recycleFolder,true));for(int i=0;i<30&&!Directory.Exists(recycleFolder);i++)await Task.Delay(100);}}catch(Exception e){Console.WriteLine("LIMITATION Owned recycled folder may require manual restore: "+recycleFolder+" ("+e.GetType().Name+")");}
            string fixtures=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"fixtures"))+Path.DirectorySeparatorChar;
            if(!root.StartsWith(fixtures,StringComparison.OrdinalIgnoreCase))throw new IOException("Fixture cleanup scope changed.");
            Directory.Delete(root,true);
        }
    }
}

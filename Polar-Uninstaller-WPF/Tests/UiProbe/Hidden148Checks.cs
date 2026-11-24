using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static partial class Parts146Checks
{
    internal static int RunHidden148()
    {
        Setup("Hidden148");
        string folder=Path.Combine(Path.GetTempPath(),"Polar148-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string AddFolder(string name,FileAttributes attributes=0)
        {
            string path=Directory.CreateDirectory(Path.Combine(folder,name)).FullName;
            File.SetAttributes(path,File.GetAttributes(path)|attributes);
            File.WriteAllBytes(Path.Combine(path,"sample.bin"),new byte[10]);
            return path;
        }
        string hidden=AddFolder("hidden",FileAttributes.Hidden);
        string system=AddFolder("system",FileAttributes.System);
        string combined=AddFolder("both",FileAttributes.Hidden|FileAttributes.System);
        string dot=AddFolder(".dot");
        string hiddenFile=Path.Combine(folder,"hidden.bin");
        File.WriteAllBytes(hiddenFile,new byte[4]);
        File.SetAttributes(hiddenFile,FileAttributes.Hidden);
        var browser=new FileBrowserService();
        var off=browser.ListAsync(folder);var on=browser.ListAsync(folder,true,default);
        Wait(off);Wait(on);
        Check(off.Result.Select(entry=>entry.Name).SequenceEqual(new[]{".dot"}),"Default listing hides both attributes but not a dot-prefixed name");
        Check(on.Result.Count==5 && on.Result.Count(entry=>entry.IsHiddenOrSystem)==4 &&
            on.Result.Where(entry=>entry.IsHiddenOrSystem).All(entry=>entry.DisplayName!=entry.Name),"Toggle shows and marks hidden and system files and folders");
        Check(FolderSizeService.Measure(folder,default) is (44,0,true),"Size includes all hidden and system bytes");

        var settingsPath=Path.Combine(root,"hidden-settings");
        var settings=new SettingsService(settingsPath);
        Check(!settings.Load().ShowHiddenItems,"Missing preference defaults to off");
        var log=new ActivityLogService(app.Dispatcher);
        var view=new FileBrowserView(false,folder,log,settings:settings);
        Wait(view.ShowFolderAsync(folder));
        var list=(ListBox)view.FindName("Files");
        Check(list.Items.Count==1,"Offscreen view initially hides hidden rows");
        var enable=(Task)Invoke(view,"ToggleHiddenItemsAsync")!;
        Wait(enable);
        var selected=list.Items.Cast<BrowserEntry>().Single(entry=>entry.FullPath==hiddenFile);
        list.SelectedItem=selected;
        Check(list.SelectedItems.Count==1&&BrowserOperationsService.CollapseNestedPaths(new[]{selected.FullPath}).Single()==hiddenFile,
            "Hidden row can be selected through the normal list");
        Check(new SettingsService(settingsPath).Load().ShowHiddenItems,"Toggle persists through atomic settings");
        var disable=(Task)Invoke(view,"ToggleHiddenItemsAsync")!;
        var enableAgain=(Task)Invoke(view,"ToggleHiddenItemsAsync")!;
        Wait(Task.WhenAll(disable,enableAgain));
        Check(list.Items.Count==5&&new SettingsService(settingsPath).Load().ShowHiddenItems,"Rapid toggles keep the newest list and preference");

        Check(ExplorerShortcuts.Resolve(Key.H,ModifierKeys.Control,false,false,true)==ExplorerCommand.ToggleHidden&&
            ExplorerShortcuts.Resolve(Key.H,ModifierKeys.Control,true,false,true)==ExplorerCommand.None&&
            ExplorerShortcuts.Resolve(Key.H,ModifierKeys.Control,false,true,true)==ExplorerCommand.None&&
            ExplorerShortcuts.Help(false).Any(row=>row.Keys=="Ctrl+H"),"Ctrl+H is shown in help and scoped to File Browser outside text input");
        foreach(bool arabic in new[]{false,true})
        {
            Check(new[]{"ShowHiddenItems","HiddenSizesTooltip","HiddenDeleteWarning","LinkTargetSize","LinkTargetSizeUnavailable"}.All(key=>!string.IsNullOrWhiteSpace(BrowserText.Get(key,arabic))),
                "Toggle, tooltip and warning are localized (Arabic="+arabic+")");
            var dialog=new PermanentDeleteDialog(arabic,1,4,false,new[]{hiddenFile},new Style(typeof(Button)),new Style(typeof(Button)),true);
            Check(((StackPanel)dialog.Content).Children.OfType<TextBlock>().Any(b=>b.Text==BrowserText.Get("HiddenDeleteWarning",arabic)),"Permanent delete dialog shows the hidden warning (Arabic="+arabic+")");
        }

        string longPath=dot;
        for(int i=0;i<9;i++)longPath=Directory.CreateDirectory(Path.Combine(longPath,"long-path-"+new string('x',29))).FullName;
        string longFile=Path.Combine(longPath,"tail.bin");
        File.WriteAllBytes(longFile,new byte[5]);
        File.SetAttributes(longFile,FileAttributes.System);
        var longListing=browser.ListAsync(longPath,true,default);Wait(longListing);
        Check(longFile.Length>260&&longListing.Result.Single().FullPath==longFile&&FolderSizeService.Measure(dot,default).Bytes==15,"Long path lists and measures system content");

        string denied=AddFolder("denied");
        var directory=new DirectoryInfo(denied);
        var acl=directory.GetAccessControl();
        var rule=new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,FileSystemRights.ListDirectory,AccessControlType.Deny);
        acl.AddAccessRule(rule);
        directory.SetAccessControl(acl);
        try
        {
            var result=FolderSizeService.Measure(folder,default);
            Check(result.Accessible&&result.Skipped>0&&result.Bytes==49,"ACL-denied child reports a partial result, retaining readable bytes");
            bool rejected=false;
            try { Wait(browser.OpenFolderAsync(denied,true,default)); }
            catch(UnauthorizedAccessException) { rejected=true; }
            Check(rejected,"An inaccessible folder reports an error instead of opening as empty");
        }
        finally
        {
            acl.RemoveAccessRuleSpecific(rule);
            directory.SetAccessControl(acl);
        }

        var operations=new BrowserOperationsService();
        bool Protected(string path)
        {
            try{BrowserOperationsService.ValidateDeletion(path);return false;}
            catch(DeletionProtectionException){return true;}
        }
        string volume=Path.GetPathRoot(folder)!;
        var paths=new List<string>{volume,Path.Combine(volume,"System Volume Information"),Path.Combine(volume,"$Recycle.Bin","entry"),
            Path.Combine(volume,"Recovery"),Path.Combine(volume,"pagefile.sys"),Path.Combine(volume,"hiberfil.sys"),Path.Combine(volume,"swapfile.sys")};
        foreach(var key in new[]{Environment.SpecialFolder.Windows,Environment.SpecialFolder.ProgramFiles,Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.UserProfile,Environment.SpecialFolder.LocalApplicationData,Environment.SpecialFolder.ApplicationData})
        {
            string target=Environment.GetFolderPath(key);
            if(target.Length>0)paths.Add(target);
        }
        var profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if(profile.Length>0)paths.Add(Path.Combine(profile,"AppData"));
        var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if(local.Length>0)paths.Add(Path.Combine(local,"PolarUninstallerWPF-Development","settings.json"));
        Check(paths.All(Protected),"Protected paths are refused without attempting deletion");
        string[] systemEntries={"eFi", "Boot", "bootmgr", "bootmgr.efi", "BOOTNXT", "WindowsApps", "WpSystem", "WUDownloadCache", "Config.Msi",
            "MSOCache", "Windows.old", "$WINDOWS.~BT", "$WINDOWS.~WS", "$WinREAgent", "$SysReset", "Documents and Settings", "DumpStack.log.tmp"};
        Check(systemEntries.SelectMany(name=>new[]{Path.Combine(volume,name),Path.Combine(volume,name,"probe")}).All(Protected),
            "The shared deletion guard rejects boot, recovery, Store, installer and upgrade roots and descendants");
        bool DirectProtected(string path)
        {
            try { DirectRemovalService.ValidateRoot(path); return false; }
            catch(DeletionProtectionException) { return true; }
        }
        Check(systemEntries.All(name=>DirectProtected(Path.Combine(volume,name,"probe"))),"Direct removal cannot target the newly protected volume locations");
        if(local.Length>0)
        {
            try
            {
                BrowserOperationsService.ValidateDeletion(Path.Combine(local,"Temp"));
                Check(true,"The existing policy still permits an ordinary AppData child");
            }
            catch(DirectoryNotFoundException) { Console.WriteLine("SKIP AppData Temp directory is absent"); }
            catch(FileNotFoundException) { Console.WriteLine("SKIP AppData Temp directory is absent"); }
        }

        string link=Path.Combine(folder,"junction");
        var start=new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,Arguments=$"/d /c mklink /J \"{link}\" \"{system}\""};
        using(var process=Process.Start(start)!)
        {
            process.WaitForExit();
            Check(process.ExitCode==0,"Fixture junction created");
        }
        Check(!FolderSizeService.Measure(link,default).Accessible&&Protected(Path.Combine(link,"sample.bin")),"Size and deletion never enter the junction target");
        var targetSize=FolderSizeService.GetDirectoryLinkTargetSizeAsync(link,false,default);Wait(targetSize);
        Check(targetSize.Result==new FolderSizeResult(10,false,false),"The browser can display all bytes from the physical junction target");
        Check(FolderSizeService.Measure(folder,default) is (59,>0,true),"The parent measurement skips the junction and never counts its target twice");
        Wait(view.ShowFolderAsync(system));
        Wait(view.ShowFolderAsync(folder));
        var linkRow=list.Items.Cast<BrowserEntry>().Single(entry=>entry.FullPath==link);
        list.SelectedItem=linkRow;
        Wait((Task)Invoke(view,"CalculateSize")!);
        Check(linkRow.Size==10&&linkRow.CountedSize is null&&linkRow.DisplaySize.EndsWith(" ↗",StringComparison.Ordinal),
            "The File Browser size action shows a marked target size and excludes it from selection totals");
        Wait((Task)Invoke(view,"OpenCurrent")!);
        var address=(TextBox)view.FindName("Address");
        Check(address.Text==system && list.Items.Cast<BrowserEntry>().Single().FullPath==Path.Combine(system,"sample.bin"),
            "Opening a directory junction displays its target path and real entries");
        Wait((Task)Invoke(view,"GoHistoryAsync",-1)!);
        Check(address.Text==folder && list.Items.Cast<BrowserEntry>().Any(entry=>entry.FullPath==link),
            "Back returns from the resolved junction target to the folder containing the junction");
        var junctionRemoval=operations.DeleteAsync(new[]{link},true,default);Wait(junctionRemoval);
        Check(junctionRemoval.Result.Single().Success&&!Directory.Exists(link)&&File.Exists(Path.Combine(system,"sample.bin")),"Permanent deletion removes the junction itself and leaves its target");
        var missing=FolderSizeService.GetDirectoryLinkTargetSizeAsync(link,false,default);Wait(missing);
        Check(missing.Result.Unavailable&&missing.Result.Bytes is null,"A missing link does not reuse a prior target size");

        string symbolic=Path.Combine(folder,"file-symlink.bin");
        try{File.CreateSymbolicLink(symbolic,hiddenFile);}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        {Console.WriteLine("SKIP File symlink creation requires Windows permission: "+error.Message);}
        if(File.Exists(symbolic))
        {
            var symlinkRemoval=operations.DeleteAsync(new[]{symbolic},true,default);Wait(symlinkRemoval);
            Check(symlinkRemoval.Result.Single().Success&&File.Exists(hiddenFile),"Permanent deletion of a file symlink preserves its target");
        }
        var recycle=operations.DeleteAsync(new[]{hiddenFile},false,default);Wait(recycle);
        Check(recycle.Result.Single().Success&&!File.Exists(hiddenFile),"Selected hidden file is moved to the Windows Recycle Bin");
        var contents=operations.DeleteAsync(new[]{Path.Combine(hidden,"sample.bin")},true,default);Wait(contents);
        Check(contents.Result.Single().Success&&Directory.Exists(hidden),"Files can be deleted inside an ordinary hidden folder");
        var permanent=operations.DeleteAsync(new[]{combined,longFile},true,default);Wait(permanent);
        Check(permanent.Result.All(item=>item.Success)&&!Directory.Exists(combined)&&!File.Exists(longFile),"Hidden+system folder and long system file can be permanently deleted");
        foreach(bool arabic in new[]{false,true})
        {
            var currentNotes=ReleaseNotesService.FindCurrent(ReleaseNotesService.Load(arabic),"1.0.148");
            Check(AppInfo.Version=="1.0.150"&&currentNotes?.Entries.Count==9,"Complete v1.0.148 notes remain embedded (Arabic="+arabic+")");
            var buildContent=typeof(ReleaseNotesView).GetMethod("BuildReleaseContent",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
            var releasePanel=(StackPanel)buildContent.Invoke(null,new object?[]{currentNotes,arabic})!;
            var columns=releasePanel.Children.OfType<WrapPanel>().Single();
            var expander=releasePanel.Children.OfType<Expander>().Single();
            Check(columns.Children.Count==3&&!expander.IsExpanded,"Three summary columns and collapsed full list are restored (Arabic="+arabic+")");
            expander.IsExpanded=true;
            var allNotes=(RichTextBox)expander.Content;
            allNotes.SelectAll();
            Check(allNotes.IsReadOnly&&allNotes.Document.FlowDirection==(arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight)&&
                currentNotes!.Entries.All(entry=>allNotes.Selection.Text.Contains(arabic?BidiText.IsolateLatin(ReleaseNotesService.DisplayEntry(entry)):ReleaseNotesService.DisplayEntry(entry)))&&
                ApplicationCommands.Copy.CanExecute(null,allNotes),"The expanded full list can be selected together (Arabic="+arabic+")");
        }
        return Finish("Hidden148");
    }
}

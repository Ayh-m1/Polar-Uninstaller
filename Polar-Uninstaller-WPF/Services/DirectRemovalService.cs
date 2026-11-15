using System.IO;

namespace PolarUninstallerWpf.Services;
public sealed record DirectRemovalResult(int Removed,int Skipped,IReadOnlyList<string> Messages,long Bytes=0)
{
    public int FoldersRemoved { get; init; }
}
public sealed class DirectRemovalService
{
    public static string Validate(InstalledProgram program,IReadOnlyList<InstalledProgram> inventory)
    {
        if(!string.IsNullOrWhiteSpace(program.Identifier))throw new IOException("Games must be removed through their verified launcher.");
        if(program.RequiresOfficialRemoval)throw new IOException("This Windows-managed installation requires its registered official uninstaller.");
        string sensitive=program.Name+" "+program.Publisher;
        if(System.Text.RegularExpressions.Regex.IsMatch(sensitive,@"(?i)\bruntime\b|\bdriver\b|antivirus|defender|security|anti.?cheat|vanguard|\bSDK\b|redistributable|\bTAP-Windows\b|\bOpenVPN\b"))throw new IOException("System components, runtimes, drivers and security software are not supported by direct removal.");
        string root=InventoryMerger.NormalizePath(program.Location);
        if(root.Length==0)throw new IOException("The exact installation folder is unknown or shared.");
        ValidateRoot(root);
        // Riot Client, Riot Vanguard and every shared Riot container are managed
        // by Riot's own uninstaller. They are never deleted directly.
        if(RiotGameService.IsProtectedComponentName(program.Name,root,program.Publisher))throw new IOException("Riot Client and Riot Vanguard are shared components and must be removed with their official uninstaller.");
        if(RiotGameService.IsSharedPath(root))throw new IOException("A shared Riot Games location cannot be removed directly.");
        bool Within(string child,string parent)=>child.Equals(parent,StringComparison.OrdinalIgnoreCase)||child.StartsWith(parent.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
        if(Within(Environment.ProcessPath??AppContext.BaseDirectory,root)||Within(AppContext.BaseDirectory,root))throw new IOException("Polar cannot remove its running installation.");
        string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if(Within(root,windows))throw new IOException("Windows directories cannot be removed.");
        var shared=new[]{Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs")};
        if(shared.Any(p=>p.Length>0&&root.Equals(p.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)))throw new IOException("A shared software directory cannot be removed.");
        var personalRoots=new[]{Environment.SpecialFolder.UserProfile,Environment.SpecialFolder.DesktopDirectory,Environment.SpecialFolder.CommonDesktopDirectory,Environment.SpecialFolder.MyDocuments,Environment.SpecialFolder.StartMenu,Environment.SpecialFolder.CommonStartMenu}.Select(Environment.GetFolderPath)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"));
        if(personalRoots.Any(p=>p.Length>0&&root.Equals(p.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)))throw new IOException("Personal root folders cannot be removed as an application installation.");
        foreach(var other in inventory)
        {
            if(UninstallVerificationService.Matches(program,other))continue;
            string path=InventoryMerger.NormalizePath(other.Location);
            if(path.Length>0&&(Within(path,root)||Within(root,path)))throw new IOException("Another application uses this installation directory.");
        }
        for(string? path=root;path!=null;path=Path.GetDirectoryName(path))
            if(Directory.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Removal through a symbolic link or junction is not supported.");
        return root;
    }

    // This guard is also used immediately before executing a captured plan.
    // In particular, never treat a drive, software container or a parent of
    // Windows/the user profile as the installation of one application.
    public static void ValidateRoot(string root)
    {
        root=Path.GetFullPath(root).TrimEnd('\\');
        bool Within(string child,string parent)=>child.Equals(parent,StringComparison.OrdinalIgnoreCase)||child.StartsWith(parent.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
        var protectedRoots=new[]{Environment.SpecialFolder.Windows,Environment.SpecialFolder.ProgramFiles,Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonProgramFiles,Environment.SpecialFolder.CommonProgramFilesX86,
            Environment.SpecialFolder.UserProfile,Environment.SpecialFolder.LocalApplicationData,Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.CommonApplicationData,Environment.SpecialFolder.DesktopDirectory,Environment.SpecialFolder.CommonDesktopDirectory,
            Environment.SpecialFolder.MyDocuments,Environment.SpecialFolder.StartMenu,Environment.SpecialFolder.CommonStartMenu}.Select(Environment.GetFolderPath)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs"))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"));
        if(root.Equals(Path.GetPathRoot(root)?.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)||protectedRoots.Any(p=>p.Length>0&&Within(p,root)))
            throw new IOException("A shared or personal root directory cannot be removed.");
        foreach(var folder in new[]{Environment.SpecialFolder.Windows,Environment.SpecialFolder.CommonProgramFiles,Environment.SpecialFolder.CommonProgramFilesX86})
        {string p=Environment.GetFolderPath(folder);if(p.Length>0&&Within(root,p))throw new IOException("Protected system or shared components cannot be removed directly.");}
        for(string? p=root;p!=null;p=Path.GetDirectoryName(p))
            if(Directory.Exists(p)&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Removal through a symbolic link or junction is not supported.");
    }
    public static IReadOnlyList<string> RunningProcesses(string root)
    {
        var result=new List<string>();
        foreach(var process in System.Diagnostics.Process.GetProcesses())
        {
            using(process)try{string? file=process.MainModule?.FileName;if(file!=null&&file.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))result.Add($"{process.ProcessName} ({process.Id})");}catch(Exception ex)when(ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException){ }
        }
        return result;
    }
    public Task<DirectRemovalResult> RemoveFilesAsync(InstalledProgram program,IReadOnlyList<InstalledProgram> inventory,IProgress<int>? progress,CancellationToken token=default)=>Task.Run(()=>
    {
        token.ThrowIfCancellationRequested();string root=Validate(program,inventory);
        if(RunningProcesses(root).Count>0)throw new IOException("Close the running application before direct removal.");
        int removed=0,skipped=0,foldersRemoved=0;long bytes=0;var messages=new List<string>();
        var pending=new Stack<string>();var directories=new List<string>();if(Directory.Exists(root))pending.Push(root);
        var clock=System.Diagnostics.Stopwatch.StartNew();
        while(pending.TryPop(out var path))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0){skipped++;continue;}
                directories.Add(path);
                foreach(var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if((entry.Attributes&FileAttributes.ReparsePoint)!=0){skipped++;continue;}
                        if(entry is DirectoryInfo child)pending.Push(child.FullName);
                        else
                        {
                            long length=0;
                            try{length=((FileInfo)entry).Length;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){ }
                            File.Delete(entry.FullName);removed++;bytes+=length;
                        }
                    }
                    catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){skipped++;if(messages.Count<100)messages.Add("Skipped: "+entry.FullName);}
                    if(clock.ElapsedMilliseconds>=150){progress?.Report(removed);clock.Restart();}
                }
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){skipped++;if(messages.Count<100)messages.Add("Skipped folder: "+path);}
        }
        foreach(var directory in directories.AsEnumerable().Reverse())
        {token.ThrowIfCancellationRequested();try{Directory.Delete(directory,false);foldersRemoved++;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){skipped++;messages.Add("Skipped folder: "+directory+" ("+RemovalEngine.Reason(ex)+")");}}
        return new DirectRemovalResult(removed,skipped,messages,bytes){FoldersRemoved=foldersRemoved};
    },token);
}

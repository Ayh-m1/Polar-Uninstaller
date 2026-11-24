using System.IO;

namespace PolarUninstallerWpf.Services;

public sealed record UninstallVerification(bool Registered, bool FolderRemaining, bool FilesRemaining, bool Incomplete = false);
public static class UninstallVerificationService
{
    public static bool AnyExecutablePresent(IEnumerable<string> paths)
    {
        foreach(string path in paths)
        {
            try{File.GetAttributes(path);return true;}
            catch(FileNotFoundException){ }catch(DirectoryNotFoundException){ }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException){return true;}
        }
        return false;
    }
    public static bool Matches(InstalledProgram original, InstalledProgram current)
    {
        if (!string.IsNullOrWhiteSpace(original.Identifier) && original.Identifier == current.Identifier) return true;
        if (!string.Equals(original.Name, current.Name, StringComparison.OrdinalIgnoreCase)) return false;
        if(!string.IsNullOrWhiteSpace(original.Publisher)&&!string.IsNullOrWhiteSpace(current.Publisher)&&!string.Equals(original.Publisher,current.Publisher,StringComparison.OrdinalIgnoreCase))return false;
        if(!string.IsNullOrWhiteSpace(original.Version)&&!string.IsNullOrWhiteSpace(current.Version)&&!string.Equals(original.Version,current.Version,StringComparison.OrdinalIgnoreCase))return false;
        if (string.IsNullOrWhiteSpace(original.Location) || string.IsNullOrWhiteSpace(current.Location))
            return string.Equals(original.Publisher, current.Publisher, StringComparison.OrdinalIgnoreCase);
        return LeftoverService.EqualPath(original.Location, current.Location);
    }
    public static UninstallVerification Assess(InstalledProgram original, IEnumerable<InstalledProgram> inventory)
    {
        bool registered = inventory.Any(p => Matches(original, p));
        bool directory = false;
        bool files = false;
        bool incomplete = false;
        if (!string.IsNullOrWhiteSpace(original.Location))
        {
            try
            {
                directory=(File.GetAttributes(original.Location)&FileAttributes.Directory)!=0;
                files=!directory||Directory.EnumerateFileSystemEntries(original.Location).Any();
                incomplete=!directory;
            }
            catch(FileNotFoundException){ }catch(DirectoryNotFoundException){ }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { directory=true;files = true; incomplete = true; }
        }
        return new(registered, directory, files, incomplete);
    }
}

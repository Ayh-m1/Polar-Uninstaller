using System.Diagnostics;
using System.IO;
namespace PolarUninstallerWpf.Services;
public sealed record OwnedDataFolder(string Path,long Bytes,int Skipped);
public static class OwnedDataService
{
    // A similar folder name alone is never sufficient evidence of ownership.
    // Require matching product AND company metadata on an executable inside it.
    public static Task<IReadOnlyList<OwnedDataFolder>> ScanAsync(InstalledProgram program,CancellationToken token)=>Task.Run<IReadOnlyList<OwnedDataFolder>>(async()=>
    {
        var results=new List<OwnedDataFolder>();string product=InstallationPathResolver.NormalizeProduct(program.Name);
        if(product.Length<3||string.IsNullOrWhiteSpace(program.Publisher))return results;
        foreach(var folder in new[]{Environment.SpecialFolder.LocalApplicationData,Environment.SpecialFolder.ApplicationData,Environment.SpecialFolder.CommonApplicationData})
        {
            token.ThrowIfCancellationRequested();string root=Environment.GetFolderPath(folder);
            try
            {
                foreach(var path in Directory.EnumerateDirectories(root))
                {
                    token.ThrowIfCancellationRequested();
                    if(InstallationPathResolver.NormalizeProduct(System.IO.Path.GetFileName(path))!=product||LeftoverService.EqualPath(path,program.Location))continue;
                    if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;
                    bool owned=false;
                    foreach(var exe in Directory.EnumerateFiles(path,"*.exe").Take(32))
                    {
                        if((File.GetAttributes(exe)&FileAttributes.ReparsePoint)!=0)continue;
                        var version=FileVersionInfo.GetVersionInfo(exe);
                        if(InstallationPathResolver.NormalizeProduct(version.ProductName??"")==product&&string.Equals(version.CompanyName?.Trim(),program.Publisher.Trim(),StringComparison.OrdinalIgnoreCase)){owned=true;break;}
                    }
                    if(owned){var size=await new BrowserOperationsService().SizeAsync(path,token);results.Add(new(path,size.Bytes,size.Skipped));}
                }
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException){ }
        }
        return results;
    },token);
}

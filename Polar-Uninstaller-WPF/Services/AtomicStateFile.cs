using System.IO;
namespace PolarUninstallerWpf.Services;
public static class AtomicStateFile
{
    public static void Write(string path,string text,bool backup=true,Action<string,string>? writeTemporary=null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            (writeTemporary??File.WriteAllText)(temp,text);
            if(backup&&File.Exists(path))File.Copy(path,path+".bak",true);
            File.Move(temp,path,true);
        }
        finally{try{if(File.Exists(temp))File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}

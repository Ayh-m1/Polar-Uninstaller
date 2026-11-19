using System.IO;

namespace PolarUninstallerWpf.Services;

public sealed record FolderHistoryMove(string Path,int Index,bool Recovered);
/// <summary>Only successful loads commit history; refreshing the same location never pushes an entry.</summary>
public sealed class FolderNavigationHistory
{
    public List<string> Entries {get;}=new();
    public int Position {get;private set;}
    public bool CanBack=>Position>0;
    public bool CanForward=>Position+1<Entries.Count;
    public string Current=>Entries[Position];
    public FolderNavigationHistory(string initial){Entries.Add(Normalize(initial));}
    private static string Normalize(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public void Commit(string path,int? index=null)
    {
        path=Normalize(path);
        if(index is >=0&&index<Entries.Count)
        {
            Position=index.Value;Entries[Position]=path;
            while(Position>0&&Entries[Position-1].Equals(path,StringComparison.OrdinalIgnoreCase)){Entries.RemoveAt(Position-1);Position--;}
            while(Position+1<Entries.Count&&Entries[Position+1].Equals(path,StringComparison.OrdinalIgnoreCase))Entries.RemoveAt(Position+1);
            return;
        }
        if(Current.Equals(path,StringComparison.OrdinalIgnoreCase))return;
        if(CanForward)Entries.RemoveRange(Position+1,Entries.Count-Position-1);
        Entries.Add(path);Position=Entries.Count-1;
    }
    public FolderHistoryMove? Find(int direction,Func<string,bool>? exists=null)
    {
        if(direction is not (-1 or 1))throw new ArgumentOutOfRangeException(nameof(direction));
        exists??=Directory.Exists;
        for(int index=Position+direction;index>=0&&index<Entries.Count;index+=direction)
        {
            string original=Entries[index];string? target=original;
            while(target!=null&&!exists(target))target=Path.GetDirectoryName(target);
            if(target==null||target.Equals(Current,StringComparison.OrdinalIgnoreCase))
            {Entries.RemoveAt(index);if(index<Position)Position--;if(direction>0)index--;continue;}
            return new(target,index,!target.Equals(original,StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }
}

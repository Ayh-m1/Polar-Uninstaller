using System.IO;

namespace PolarUninstallerWpf.Services;

public sealed record FolderAggregate(string Path,long Bytes,int Files,int Folders);
public static class StorageCacheUpdates
{
    public static bool Within(string path,string root)=>path.Equals(root,StringComparison.OrdinalIgnoreCase)||path.StartsWith(root.EndsWith(Path.DirectorySeparatorChar)?root:root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    public static StorageSnapshot Apply(StorageSnapshot snapshot,FileChangeBatch batch)
    {
        string Normalize(string value)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        var changes=batch.Changes.Where(c=>c.Success&&Path.IsPathFullyQualified(c.Path)).Select(c=>c with{Path=Normalize(c.Path)}).Where(c=>(Within(c.Path,snapshot.Root)||Within(snapshot.Root,c.Path))&&(batch.Kind==FileChangeKind.HiddenFromDashboard||!snapshot.RemovedPaths.Any(p=>Within(c.Path,p)))).DistinctBy(c=>c.Path,StringComparer.OrdinalIgnoreCase).ToArray();
        if(changes.Length==0||snapshot.AppliedChanges.Contains(batch.Id))return snapshot;
        var paths=changes.Select(c=>c.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Removed(string p)
        {
            for(string? cursor=Normalize(p);cursor!=null;cursor=Path.GetDirectoryName(cursor))if(paths.Contains(cursor))return true;
            return false;
        }
        var applied=snapshot.AppliedChanges.Append(batch.Id).TakeLast(1024).ToArray();
        if(batch.Kind==FileChangeKind.HiddenFromDashboard)
            return snapshot with{HiddenPaths=snapshot.HiddenPaths.Concat(paths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),AppliedChanges=applied};
        var removedPaths=snapshot.RemovedPaths.Concat(paths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(Removed(snapshot.Root))return snapshot with{FileIndex=[],FolderIndex=[],Largest=[],LargestFolders=[],TotalBytes=0,FilesScanned=0,FoldersScanned=0,AppliedChanges=applied,RemovedPaths=removedPaths};
        var files=snapshot.FileIndex.Where(f=>!Removed(f.Path)).ToArray();
        var folders=snapshot.FolderIndex.Where(f=>!Removed(f.Path)).ToDictionary(f=>f.Path,f=>f,StringComparer.OrdinalIgnoreCase);
        long removedBytes=0;int removedFiles=0,removedFolders=0;bool uncertain=false;
        if(snapshot.IndexComplete)
        {
            removedBytes=snapshot.FileIndex.Where(f=>Removed(f.Path)).Sum(f=>f.Bytes);
            removedFiles=snapshot.FileIndex.Count-files.Length;removedFolders=snapshot.FolderIndex.Count-folders.Count;
            foreach(string p in folders.Keys.ToArray())folders[p]=folders[p] with{Bytes=0,Files=0,Folders=1};
            foreach(var file in files)
                for(string? parent=Path.GetDirectoryName(file.Path);parent!=null&&Within(parent,snapshot.Root);parent=Path.GetDirectoryName(parent))
                    if(folders.TryGetValue(parent,out var f))folders[parent]=f with{Bytes=f.Bytes+file.Bytes,Files=f.Files+1};
            foreach(string p in folders.Keys.ToArray())
                for(string? parent=Path.GetDirectoryName(p);parent!=null&&Within(parent,snapshot.Root);parent=Path.GetDirectoryName(parent))
                    if(folders.TryGetValue(parent,out var f))folders[parent]=f with{Folders=f.Folders+1};
        }
        else
        {
            // Large scans keep a bounded file index, but retain exact aggregates for every folder.
            // A deletion outside that index supplies its captured pre-deletion byte count.
            var roots=changes.Where(c=>!changes.Any(other=>other.Path!=c.Path&&Within(c.Path,other.Path))).ToArray();
            foreach(var item in roots)
            {
                var folder=snapshot.FolderIndex.FirstOrDefault(f=>f.Path.Equals(item.Path,StringComparison.OrdinalIgnoreCase));
                var file=snapshot.FileIndex.FirstOrDefault(f=>f.Path.Equals(item.Path,StringComparison.OrdinalIgnoreCase));
                long size=folder?.Bytes??file?.Bytes??item.Bytes??0;int count=folder?.Files??(item.IsDirectory?0:1),dirs=folder?.Folders??(item.IsDirectory?1:0);
                if(folder==null&&file==null&&(item.Bytes==null||batch.At<snapshot.CompletedAt)){uncertain=true;continue;}
                removedBytes+=size;removedFiles+=count;removedFolders+=dirs;
                for(string? parent=Path.GetDirectoryName(item.Path);parent!=null&&Within(parent,snapshot.Root);parent=Path.GetDirectoryName(parent))
                    if(folders.TryGetValue(parent,out var f))folders[parent]=f with{Bytes=Math.Max(0,f.Bytes-size),Files=Math.Max(0,f.Files-count),Folders=Math.Max(1,f.Folders-dirs)};
            }
        }
        return snapshot with{FileIndex=files,FolderIndex=folders.Values.ToArray(),Largest=files.OrderByDescending(f=>f.Bytes).Take(500).ToArray(),
            LargestFolders=folders.Values.Where(f=>string.Equals(Path.GetDirectoryName(f.Path),snapshot.Root,StringComparison.OrdinalIgnoreCase)).Select(f=>new LargeFile(Path.GetFileName(f.Path),f.Path,f.Bytes)).OrderByDescending(f=>f.Bytes).ToArray(),
            TotalBytes=Math.Max(0,snapshot.TotalBytes-removedBytes),FilesScanned=Math.Max(0,snapshot.FilesScanned-removedFiles),FoldersScanned=Math.Max(0,snapshot.FoldersScanned-removedFolders),
            Limited=snapshot.Limited||uncertain,AppliedChanges=applied,RemovedPaths=removedPaths};
    }
}

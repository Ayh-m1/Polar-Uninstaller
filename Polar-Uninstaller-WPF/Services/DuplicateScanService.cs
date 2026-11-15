using System.IO;
using System.Security.Cryptography;

namespace PolarUninstallerWpf.Services;

public sealed record DuplicateFile(string Path, long Size, DateTime Modified);
public sealed record DuplicateGroup(string Hash, long Size, IReadOnlyList<DuplicateFile> Files)
{
    public long Reclaimable => Size * (Files.Count - 1);
}
public sealed record DuplicateScanResult(IReadOnlyList<DuplicateGroup> Groups, int Scanned, int Skipped)
{
    public string Root { get; init; } = "";
}
public sealed record DuplicateProgress(string Phase, int Count, int Compared=0, int Total=0, int Skipped=0);

public sealed class DuplicateScanService
{
    public Task<DuplicateScanResult> ScanAsync(string root, IProgress<DuplicateProgress>? progress, CancellationToken token,IProgress<DuplicateGroup>? verifiedGroups=null) => Task.Run(async () =>
    {
        var directory = new DirectoryInfo(root);
        if (!directory.Exists) throw new DirectoryNotFoundException();
        for(var parent=directory;parent!=null;parent=parent.Parent)
            if((parent.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("The scan path cannot pass through a link.");
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("The scan root cannot be a link.");
        var pending = new Stack<string>(); pending.Push(directory.FullName);
        var bySize = new Dictionary<long, List<DuplicateFile>>(); int scanned = 0, skipped = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int total=0;
        void Report(string phase, int count, int compared=0) { if (watch.ElapsedMilliseconds >= 150) { progress?.Report(new(phase, count,compared,total,skipped)); watch.Restart(); } }
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var current = new DirectoryInfo(pending.Pop());
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                foreach (var item in current.EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if ((item.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                        if (item is DirectoryInfo folder) pending.Push(folder.FullName);
                        else if (item is FileInfo file)
                        {
                            scanned++;
                            if (!bySize.TryGetValue(file.Length, out var group)) bySize[file.Length] = group = new();
                            group.Add(new(file.FullName, file.Length, file.LastWriteTimeUtc)); Report("enumerating", scanned);
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { skipped++; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { skipped++; }
        }
        total=bySize.Values.Where(x=>x.Count>1).Sum(x=>x.Count);
        progress?.Report(new("hashing",scanned,0,total,skipped));
        var matches = new List<DuplicateGroup>(); int hashed = 0;
        foreach (var candidates in bySize.Values.Where(x => x.Count > 1))
        {
            var hashes = new Dictionary<string, List<DuplicateFile>>();
            foreach (var file in candidates)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(file.Path);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length != file.Size || info.LastWriteTimeUtc != file.Modified) { skipped++; continue; }
                    await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                    info.Refresh(); if (info.Length != file.Size || info.LastWriteTimeUtc != file.Modified) { skipped++; continue; }
                    if (!hashes.TryGetValue(hash, out var group)) hashes[hash] = group = new();
                    group.Add(file); Report("hashing", scanned,++hashed);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { skipped++; }
            }
            foreach (var (hash, files) in hashes.Where(x => x.Value.Count > 1))
            {var group=new DuplicateGroup(hash,files[0].Size,files);matches.Add(group);verifiedGroups?.Report(group);}
        }
        progress?.Report(new("complete",scanned,hashed,total,skipped));
        return new DuplicateScanResult(matches.OrderByDescending(x => x.Reclaimable).ToArray(), scanned, skipped){Root=directory.FullName};
    }, token);
}

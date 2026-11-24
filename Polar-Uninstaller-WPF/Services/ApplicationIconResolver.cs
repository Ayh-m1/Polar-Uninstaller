using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PolarUninstallerWpf.Services;

/// <summary>
/// Resolution order: the pre-Part-C in-process resolver (known-good in v1.0.144)
/// first, then the isolated worker chain (registry / main EXE / MSI / Store /
/// shell / launchers) as extra fallbacks. The letter badge is produced by the UI
/// when this method returns null. Only real decoded icons are ever cached.
/// </summary>
public static class ApplicationIconResolver
{
    private sealed record Cached(ProgramIcon Icon,string Fingerprint);
    private static readonly ConcurrentDictionary<string,Cached> Memory=new();
    private static readonly ConcurrentDictionary<string,(long At,string Reason,string Detail)> Failures=new();
    private static readonly TimeSpan FailureCooldown=TimeSpan.FromSeconds(60);
    // A slow legacy/scanner scan must never hold a row; the UI shows the badge for
    // this item and the next scan/app start retries it.
    private static readonly TimeSpan LegacyTimeout=TimeSpan.FromSeconds(15);
    private static readonly TimeSpan WorkerTimeout=TimeSpan.FromSeconds(15);

    public static ProgramIcon? TryLoadCached(InstalledProgram p)=>null; // Never perform disk reads on row creation.

    /// <summary>Called at the start of every scan so failures are retried immediately.</summary>
    public static void ResetFailures()=>Failures.Clear();

    public static async Task<ProgramIcon?> ResolveAsync(InstalledProgram p,int pixels=64,CancellationToken token=default,bool priority=true)
    {
        pixels=Math.Clamp(pixels,32,256);string key=ProgramIconDiskCache.KeyFor(p)+"|"+pixels;
        return await Task.Run(async()=>
        {
            token.ThrowIfCancellationRequested();
            if(Memory.TryGetValue(key,out var retained))
            {
                try
                {
                    if(retained.Fingerprint==ProgramIconDiskCache.SourceKey(retained.Icon.SourcePath,retained.Icon.SourceIndex,pixels)&&File.Exists(retained.Icon.SourcePath)
                        &&(retained.Icon.Method==IconResolutionMethod.Legacy||IconSourceGuard.IsOwned(p,retained.Icon.SourcePath)))
                    {
                        IconDiagnostics.Report(p,true,"memory","");
                        return retained.Icon;
                    }
                }catch(IOException){ }
                Memory.TryRemove(key,out _);
            }
            var cached=ProgramIconDiskCache.TryLoad(p,pixels);
            if(cached!=null)
            {
                if(cached.Method==IconResolutionMethod.Legacy||IconSourceGuard.IsOwned(p,cached.SourcePath))
                {
                    // A disk-cache hit is still a resolved card and must be counted.
                    IconDiagnostics.Report(p,true,"cache","");
                    return Store(p,cached,key,pixels);
                }
                // An older build may have cached another product's logo. Discard it
                // instead of displaying a wrong icon, then retry the real sources.
                ProgramIconDiskCache.Remove(p);
            }

            // Failures are only a short in-process cooldown. They are never written
            // to disk, so a fresh app start (or Refresh) always retries them.
            if(Failures.TryGetValue(key,out var failed)&&Environment.TickCount64-failed.At<(long)FailureCooldown.TotalMilliseconds)
            {
                IconDiagnostics.Report(p,false,failed.Reason,failed.Detail+" (retry cooldown)");
                return null;
            }

            var legacy=await RunLegacyAsync(p,token).ConfigureAwait(false);
            if(legacy!=null)
            {
                IconDiagnostics.Report(p,true,"legacy","");
                return Store(p,Fit(legacy,pixels),key,pixels);
            }

            string reason="no_source",detail="no legacy or worker source produced a usable icon";
            Task<IconWorkerReply>? pending=null;
            try
            {
                pending=IconWorkerPool.ResolveAsync(p,pixels,token,priority);
                var reply=await pending.WaitAsync(WorkerTimeout,token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if(reply.Png is {Length:>0})
                {
                    // Final ownership guard: a decoded logo from another product or
                    // package is discarded like a failure, never shown.
                    if(!IconSourceGuard.IsOwned(p,reply.Source))
                    {
                        reason="unowned";
                        detail=$"source does not belong to the entry: {reply.Source}";
                    }
                    else
                    {
                        using var stream=new MemoryStream(reply.Png);var bitmap=new BitmapImage();
                        bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();
                        var icon=new ProgramIcon(bitmap,null){SourcePath=reply.Source,SourceIndex=reply.Index,Method=reply.Method};
                        IconDiagnostics.Report(p,true,"worker","");
                        return Store(p,icon,key,pixels);
                    }
                }
                else
                {
                    reason=string.IsNullOrWhiteSpace(reply.Reason)?"no_source":reply.Reason;
                    detail=reply.Detail+"; tried="+reply.Tried;
                }
            }
            catch(TimeoutException)
            {
                reason="timeout";detail=$"icon worker exceeded {WorkerTimeout.TotalSeconds:0}s";
                if(pending!=null)Observe(pending);
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
            catch(Exception e) when(e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException or System.Text.Json.JsonException)
            {
                reason="worker_start";detail=$"{e.GetType().Name}: {e.Message}";
            }

            Failures[key]=(Environment.TickCount64,reason,detail);
            IconDiagnostics.Report(p,false,reason,detail);
            return null;
        },token).ConfigureAwait(false);
    }

    private static async Task<ProgramIcon?> RunLegacyAsync(InstalledProgram p,CancellationToken token)
    {
        try
        {
            var task=LegacyApplicationIconResolver.ResolveAsync(p);
            return await task.WaitAsync(LegacyTimeout,token).ConfigureAwait(false);
        }
        catch(TimeoutException e)
        {
            CrashDiagnostics.Record("Legacy icon timeout",e);
            return null;
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException or System.Security.SecurityException)
        {
            CrashDiagnostics.Record("Legacy icon resolver",e);
            return null;
        }
    }

    private static void Observe(Task<IconWorkerReply> task)=>_=task.ContinueWith(t=>_=t.Exception,TaskContinuationOptions.OnlyOnFaulted);

    /// <summary>The legacy decoder always returns its native size; honor the DPI request.</summary>
    private static ProgramIcon Fit(ProgramIcon icon,int pixels)
    {
        if(icon.Image is not BitmapSource source)return icon;
        int largest=Math.Max(source.PixelWidth,source.PixelHeight);
        if(largest<=pixels)return icon;
        double scale=pixels/(double)largest;
        var scaled=new TransformedBitmap(source,new ScaleTransform(scale,scale));
        var converted=new FormatConvertedBitmap(scaled,PixelFormats.Bgra32,null,0);converted.Freeze();
        return icon with{Image=converted};
    }

    private static ProgramIcon Store(InstalledProgram p,ProgramIcon icon,string key,int pixels)
    {
        ProgramIconDiskCache.Save(p,icon,pixels);
        if(Memory.Count>=256)foreach(string stale in Memory.Keys.Take(32))Memory.TryRemove(stale,out _);
        try{Memory[key]=new(icon,ProgramIconDiskCache.SourceKey(icon.SourcePath,icon.SourceIndex,pixels));}catch(IOException){ }
        return icon;
    }
}

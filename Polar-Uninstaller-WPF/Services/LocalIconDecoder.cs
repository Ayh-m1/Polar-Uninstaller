using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PolarUninstallerWpf.Services;

public static class LocalIconDecoder
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct ShellInfo{public IntPtr Icon;public int Index;public uint Attributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string Type;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint PrivateExtractIconsW(string file,int index,int width,int height,out IntPtr icon,out uint id,uint count,uint flags);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr SHGetFileInfo(string path,uint attributes,out ShellInfo info,uint size,uint flags);
    [ComImport,Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IImageList
    {
        void Add();void ReplaceIcon();void SetOverlayImage();void Replace();void AddMasked();void Draw();void Remove();
        [PreserveSig]int GetIcon(int index,int flags,out IntPtr icon);
    }
    [DllImport("shell32.dll",EntryPoint="SHGetImageList")]private static extern int SHGetImageList(int type,ref Guid iid,out IImageList imageList);
    public static BitmapSource? Load(LocalIconSource candidate,int pixels)
    {
        try
        {
            if(!LocalIconSources.TryParseDisplayIcon(candidate.Path,out var file,out _)||!File.Exists(file)&&!Directory.Exists(file))return null;
            pixels=Math.Clamp(pixels,32,256);string ext=Path.GetExtension(file).ToLowerInvariant();BitmapSource? source=null;
            if(!candidate.Shell&&new[]{".ico",".png",".jpg",".jpeg",".bmp"}.Contains(ext))
            {
                if(new FileInfo(file).Length>32*1024*1024)return null;
                using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                if(ext==".ico")
                {
                    var frames=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames.Where(f=>(long)f.PixelWidth*f.PixelHeight<=16*1024*1024).ToArray();
                    source=frames.Where(f=>f.PixelWidth>=pixels).OrderBy(f=>f.PixelWidth).FirstOrDefault()??frames.OrderByDescending(f=>f.PixelWidth).FirstOrDefault();
                }
                else
                {
                    var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.OnDemand);var frame=decoder.Frames[0];
                    if((long)frame.PixelWidth*frame.PixelHeight>16*1024*1024)return null;
                    stream.Position=0;var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=Math.Min(pixels,frame.PixelWidth);image.StreamSource=stream;image.EndInit();source=image;
                }
            }
            else if(!candidate.Shell&&new[]{".exe",".dll"}.Contains(ext))
            {
                if(new FileInfo(file).Length>512L*1024*1024)return null;
                uint count=PrivateExtractIconsW(file,candidate.Index,pixels,pixels,out var handle,out _,1,0);
                if(count is 0 or uint.MaxValue||handle==IntPtr.Zero)return null;
                try{source=Imaging.CreateBitmapSourceFromHIcon(handle,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());source.Freeze();}finally{DestroyIcon(handle);}
            }
            else if(candidate.Shell)
            {
                if(SHGetFileInfo(file,0,out var info,(uint)Marshal.SizeOf<ShellInfo>(),0x100|0x4000)==IntPtr.Zero)return null;
                // Do not return the generic Windows application/document square.
                try
                {
                    bool generic=false;
                    foreach(string dummy in new[]{"PolarUnknown.exe","PolarUnknown.unrecognized"})
                        if(SHGetFileInfo(dummy,0x80,out var fallback,(uint)Marshal.SizeOf<ShellInfo>(),0x10|0x100|0x4000)!=IntPtr.Zero)
                        {generic|=info.Index==fallback.Index;if(fallback.Icon!=IntPtr.Zero)DestroyIcon(fallback.Icon);}
                    if(generic)return null;
                    Guid iid=typeof(IImageList).GUID;
                    if(SHGetImageList(pixels>48?4:2,ref iid,out var list)>=0)
                    {
                        try{if(list.GetIcon(info.Index,1,out var big)>=0&&big!=IntPtr.Zero)try{source=Imaging.CreateBitmapSourceFromHIcon(big,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());source.Freeze();}finally{DestroyIcon(big);}}
                        finally{Marshal.ReleaseComObject(list);}
                    }
                    source??=Imaging.CreateBitmapSourceFromHIcon(info.Icon,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());source.Freeze();
                }
                finally{if(info.Icon!=IntPtr.Zero)DestroyIcon(info.Icon);}
            }
            if(source==null)return null;
            double scale=Math.Min(1,pixels/(double)Math.Max(source.PixelWidth,source.PixelHeight));
            if(scale<1)source=new TransformedBitmap(source,new ScaleTransform(scale,scale));
            var frozen=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);frozen.Freeze();
            var data=new byte[frozen.PixelWidth*frozen.PixelHeight*4];frozen.CopyPixels(data,frozen.PixelWidth*4,0);
            return data.Where((_,i)=>i%4==3).Any(a=>a>0)?frozen:null;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or COMException or System.Security.SecurityException){return null;}
    }
}

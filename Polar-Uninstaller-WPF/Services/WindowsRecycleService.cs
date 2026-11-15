using System.IO;
using System.Runtime.InteropServices;

namespace PolarUninstallerWpf.Services;

/// <summary>Windows Shell recycling only. No temporary files and no permanent
/// deletion fallback. Each original item is submitted as an IShellItem on STA.</summary>
public static class WindowsRecycleService
{
    internal static Task RecycleAsync(string path,CancellationToken token)
    {
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            try { Recycle(path,token);completion.SetResult(); }
            catch(OperationCanceledException){completion.TrySetCanceled(token);}
            catch(Exception e){completion.TrySetException(e);}
        }){IsBackground=true,Name="Polar Shell recycle"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;
    }

    private static void Recycle(string path,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();BrowserOperationsService.ValidateDeletion(path);
        // Network/removable targets are not silently converted into permanent
        // deletion if their Shell provider cannot offer normal recycling.
        if(new DriveInfo(Path.GetPathRoot(path)!).DriveType!=DriveType.Fixed)
            throw new IOException("Recycle Bin support is not verified for this volume. No permanent-delete fallback was used.");
        IFileOperation? operation=null;IShellItem? item=null;
        var sink=new DeleteSink(token,path);
        try
        {
            operation=(IFileOperation)new FileOperation();
            // RECYCLEONDELETE/ADDUNDORECORD: Windows 8+, including 10 and 11.
            // NO_CONNECTED_ELEMENTS ensures associated, unselected HTML folders
            // are not deleted as a side effect of selecting a single file.
            operation.SetOperationFlags(0x0040|0x00080000|0x20000000|0x00100000|0x0400|0x0010|0x0004|0x2000);
            Guid iid=typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(path,IntPtr.Zero,ref iid,out item);
            operation.Advise(sink,out uint cookie);
            operation.DeleteItem(item,null);operation.PerformOperations();
            operation.Unadvise(cookie);
            operation.GetAnyOperationsAborted(out bool aborted);
            token.ThrowIfCancellationRequested();
            if(sink.Error<0)Marshal.ThrowExceptionForHR(sink.Error);
            if(aborted)throw new IOException("Windows did not complete recycling.");
            if(!sink.Recycled)throw new IOException("Windows did not return a recycled destination; recycling could not be verified (callbacks="+sink.PreCount+"/"+sink.PostCount+", flags=0x"+sink.TransferFlags.ToString("X8")+").");
            BrowserOperationsService.VerifyAbsent(path);
        }
        finally
        {
            if(item!=null)Marshal.FinalReleaseComObject(item);
            if(operation!=null)Marshal.FinalReleaseComObject(operation);
            GC.KeepAlive(sink);
        }
    }

    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)]
    private static extern void SHCreateItemFromParsingName(string path,IntPtr context,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IShellItem item);
    [ComImport,Guid("3AD05575-8857-4850-9277-11B85BDB8E09")]private class FileOperation { }
    [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        void BindToHandler(IntPtr context,ref Guid handler,ref Guid iid,out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint kind,out IntPtr name);
        void GetAttributes(uint mask,out uint attributes);
        void Compare(IShellItem other,uint hint,out int order);
    }
    [ComImport,Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IFileOperationProgressSink sink,out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)]string message);
        void SetProgressDialog([MarshalAs(UnmanagedType.IUnknown)]object dialog);
        void SetProperties([MarshalAs(UnmanagedType.IUnknown)]object properties);
        void SetOwnerWindow(IntPtr window);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)]object items);
        void RenameItem(IShellItem item,[MarshalAs(UnmanagedType.LPWStr)]string name,IFileOperationProgressSink? sink);
        void RenameItems([MarshalAs(UnmanagedType.IUnknown)]object items,[MarshalAs(UnmanagedType.LPWStr)]string name);
        void MoveItem(IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string? name,IFileOperationProgressSink? sink);
        void MoveItems([MarshalAs(UnmanagedType.IUnknown)]object items,IShellItem destination);
        void CopyItem(IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string? name,IFileOperationProgressSink? sink);
        void CopyItems([MarshalAs(UnmanagedType.IUnknown)]object items,IShellItem destination);
        void DeleteItem(IShellItem item,IFileOperationProgressSink? sink);
        void DeleteItems([MarshalAs(UnmanagedType.IUnknown)]object items);
        void NewItem(IShellItem destination,uint attributes,[MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.LPWStr)]string? template,IFileOperationProgressSink? sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)]out bool aborted);
    }
    [ComVisible(true),Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig]int StartOperations();[PreserveSig]int FinishOperations(int result);
        [PreserveSig]int PreRenameItem(uint flags,IShellItem item,[MarshalAs(UnmanagedType.LPWStr)]string name);
        [PreserveSig]int PostRenameItem(uint flags,IShellItem item,[MarshalAs(UnmanagedType.LPWStr)]string name,int result,IShellItem? created);
        [PreserveSig]int PreMoveItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string? name);
        [PreserveSig]int PostMoveItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string? name,int result,IShellItem? created);
        [PreserveSig]int PreCopyItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string? name);
        [PreserveSig]int PostCopyItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string? name,int result,IShellItem? created);
        [PreserveSig]int PreDeleteItem(uint flags,IShellItem item);
        [PreserveSig]int PostDeleteItem(uint flags,IShellItem item,int result,IShellItem? created);
        [PreserveSig]int PreNewItem(uint flags,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name);
        [PreserveSig]int PostNewItem(uint flags,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.LPWStr)]string? template,uint attributes,int result,IShellItem? created);
        [PreserveSig]int UpdateProgress(uint total,uint completed);[PreserveSig]int ResetTimer();[PreserveSig]int PauseTimer();[PreserveSig]int ResumeTimer();
    }
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
    public sealed class DeleteSink(CancellationToken token,string selectedPath):IFileOperationProgressSink
    {
        public bool Recycled;public int Error;public int PreCount,PostCount;public uint TransferFlags;
        private int Continue()=>token.IsCancellationRequested?unchecked((int)0x800704C7):0;
        public int StartOperations()=>Continue();public int FinishOperations(int result){if(result<0)Error=result;return 0;}
        public int PreDeleteItem(uint flags,IShellItem item)
        {
            PreCount++;TransferFlags=flags;
            int cancelled=Continue();if(cancelled!=0)return cancelled;
            // TSF_DELETE_RECYCLE_IF_POSSIBLE must be present on the actual
            // transfer. Refuse a provider's permanent-delete path before it runs.
            if((flags&0x80)==0){Error=unchecked((int)0x80004004);return Error;}
            return 0;
        }
        public int PostDeleteItem(uint flags,IShellItem item,int result,IShellItem? created)
        {
            PostCount++;if(result<0)Error=result;
            if(result>=0&&created!=null)
            {
                IntPtr name=IntPtr.Zero;
                try
                {
                    item.GetDisplayName(0x80058000,out name); // SIGDN_FILESYSPATH
                    Recycled|=string.Equals(Marshal.PtrToStringUni(name),selectedPath,StringComparison.OrdinalIgnoreCase);
                }
                catch(COMException){ }finally{if(name!=IntPtr.Zero)Marshal.FreeCoTaskMem(name);}
            }
            return 0;
        }
        public int PreRenameItem(uint f,IShellItem i,string n)=>Continue();public int PostRenameItem(uint f,IShellItem i,string n,int r,IShellItem? c)=>0;
        public int PreMoveItem(uint f,IShellItem i,IShellItem d,string? n)=>Continue();public int PostMoveItem(uint f,IShellItem i,IShellItem d,string? n,int r,IShellItem? c)=>0;
        public int PreCopyItem(uint f,IShellItem i,IShellItem d,string? n)=>Continue();public int PostCopyItem(uint f,IShellItem i,IShellItem d,string? n,int r,IShellItem? c)=>0;
        public int PreNewItem(uint f,IShellItem d,string n)=>Continue();public int PostNewItem(uint f,IShellItem d,string n,string? t,uint a,int r,IShellItem? c)=>0;
        public int UpdateProgress(uint t,uint c)=>Continue();public int ResetTimer()=>0;public int PauseTimer()=>0;public int ResumeTimer()=>0;
    }
}

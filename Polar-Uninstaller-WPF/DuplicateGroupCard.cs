using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public sealed class DuplicateGroupCard:INotifyPropertyChanged
{
    private bool arabic,expanded,updating;
    public DuplicateGroup Source {get;}
    public IReadOnlyList<DuplicateRow> Copies {get;}
    public string Name=>Copies[0].Name;
    public long RecoverableBytes=>Source.Reclaimable;
    public long FileBytes=>Source.Size;
    public int CopyCount=>Copies.Count;
    public string CopyLabel=>T("DupCopies",CopyCount);
    public string SizeLabel=>T("DupEach",DisplayFormat.Size(FileBytes,false));
    public string RecoveryLabel=>T("DupRecoverable",DisplayFormat.Size(RecoverableBytes,false));
    public string RecoveryHeading=>T("DupRecovery")+": ";
    public string RecoveryValue=>DisplayFormat.Size(RecoverableBytes,false);
    public string FileSizeValue=>DisplayFormat.Size(FileBytes,false);
    public string EachLabel=>T("DupPerFile");
    public string Category {get;}
    public string Icon=>Category switch{"Images"=>"▧","Videos"=>"▷","Documents"=>"▤","Archives"=>"▣","Executables"=>"⚙",_=>"◇"};
    public string SelectionName=>T("DupSelectGroup",Name);
    public string ExpandName=>T(Expanded?"DupCollapse":"DupExpand",Name);
    public bool Expanded{get=>expanded;set{if(expanded==value)return;expanded=value;Changed();Changed(nameof(ExpandName));}}
    public bool? Selected
    {
        get{int selected=Copies.Count(c=>c.Selected);return selected==0?false:selected==Copies.Count-1?true:null;}
        set{updating=true;try{foreach(var row in Copies)row.Selected=value==true&&!row.IsRetained;}finally{updating=false;}NotifySelection();}
    }
    public event Action? SelectionUpdated;
    public event PropertyChangedEventHandler? PropertyChanged;
    private string T(string key,params object[] args)=>BrowserText.Get(key,arabic,args);
    public DuplicateGroupCard(DuplicateGroup source,IReadOnlyList<DuplicateRow> copies,bool rtl)
    {
        Source=source;Copies=copies;arabic=rtl;Category=CategoryFor(Name);
        foreach(var row in Copies){row.Owner=this;row.PropertyChanged+=(_,e)=>{if(!updating&&e.PropertyName==nameof(DuplicateRow.Selected))NotifySelection();};}
    }
    public void Keep(DuplicateRow row)
    {
        if(!Copies.Contains(row))return;
        updating=true;try{foreach(var copy in Copies)copy.SetRetained(ReferenceEquals(row,copy));}finally{updating=false;}NotifySelection();
    }
    private void NotifySelection(){Changed(nameof(Selected));SelectionUpdated?.Invoke();}
    public void SetLanguage(bool value){arabic=value;foreach(var row in Copies)row.SetLanguage(value);foreach(string key in new[]{nameof(CopyLabel),nameof(SizeLabel),nameof(RecoveryLabel),nameof(RecoveryHeading),nameof(EachLabel),nameof(SelectionName),nameof(ExpandName)})Changed(key);}
    private void Changed([CallerMemberName]string? key=null)=>PropertyChanged?.Invoke(this,new(key));
    public static string CategoryFor(string name)=>Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" or ".ico" or ".heic"=>"Images",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".wmv"=>"Videos",
        ".txt" or ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".csv" or ".rtf"=>"Documents",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2"=>"Archives",
        ".exe" or ".msi" or ".dll" or ".com"=>"Executables",_=>"Other"
    };
}

using System.IO;
using System.Runtime.CompilerServices;

namespace PolarUninstallerWpf.Services;

/// <summary>One preference shared by the browser and its duplicate view; never Activity Log events.</summary>
public sealed class SelectionEducationState
{
    private static readonly ConditionalWeakTable<SettingsService,SelectionEducationState> States=new();
    private readonly SettingsService settings;
    public bool Dismissed {get;private set;}
    public event Action? Changed;
    private SelectionEducationState(SettingsService settings){this.settings=settings;Dismissed=settings.Load().SelectionTipDismissed;}
    public static SelectionEducationState For(SettingsService settings)=>States.GetValue(settings,value=>new(value));
    public void Dismiss()
    {
        if(Dismissed)return;
        Dismissed=true;Changed?.Invoke();
        try{settings.Save(settings.Load() with{SelectionTipDismissed=true});}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException){System.Diagnostics.Debug.WriteLine("Selection hint preference: "+error.Message);}
    }
}

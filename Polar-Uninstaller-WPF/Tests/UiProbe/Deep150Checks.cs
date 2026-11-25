using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static partial class Parts146Checks
{
    internal static int RunDeep150()
    {
        Setup("Deep150");
        const string appId="991501501";
        string steamApps=Directory.CreateDirectory(Path.Combine(root,"fixture-steam","steamapps")).FullName;
        string location=Directory.CreateDirectory(Path.Combine(steamApps,"common","Fixture Game")).FullName;
        string cache=Directory.CreateDirectory(Path.Combine(location,"cdn","live")).FullName;
        string image=Path.Combine(cache,"retained.png");
        File.WriteAllText(image,"fixture");
        var game=new InstalledProgram("Fixture Game","Fixture","1",location,SizeKb:4,Uninstall:$"steam://uninstall/{appId}",Identifier:$"steam:{appId}");
        string manifest=Path.Combine(steamApps,$"appmanifest_{appId}.acf");
        File.WriteAllText(manifest,"fixture");
        Check(UninstallMonitorService.Probe(game,null) is { Confirmed:false,LauncherRecordPresent:true },"A Steam manifest keeps the game installed even with a passive-only folder");
        File.Delete(manifest);
        var probe=UninstallMonitorService.Probe(game,null);
        Check(probe is { Confirmed:true,RetainedSteamAssets:true }&&File.Exists(image),"Steam image-only remnants count as retained files and are never removed by verification");
        var report=UninstallReportService.FromProbe(game,probe,null,probe.Detail);
        Check(report.Outcome==UninstallOutcome.RemovedWithLeftovers&&report.RemainingPaths.Contains(location),"The report identifies the retained folder without declaring a failed uninstall");
        var inventory=new ApplicationInventoryService(_=>Task.FromResult<IReadOnlyList<InstalledProgram>>(new[]{game}));
        var refreshed=inventory.RefreshAsync();Wait(refreshed);
        Check(refreshed.Result.Count==0&&File.Exists(image),"A fresh scan suppresses the stale registered entry and preserves the files");
        string executable=Path.Combine(location,"fixture.exe");
        File.WriteAllText(executable,"fixture, never run");
        Check(UninstallMonitorService.Probe(game,null) is { Confirmed:false,RetainedSteamAssets:false },"A remaining executable prevents the passive-leftover classification");
        refreshed=inventory.RefreshAsync();Wait(refreshed);
        Check(refreshed.Result.Count==1,"The inventory keeps an entry with remaining executable payload");
        File.Delete(executable);
        string unknown=Path.Combine(location,"unknown.pak");
        File.WriteAllText(unknown,"fixture");
        Check(!UninstallMonitorService.Probe(game,null).Confirmed,"Unrecognized files keep Steam verification inconclusive");
        int gameProbes = 0;
        var gameMonitor = UninstallMonitorService.MonitorAsync(() =>
        {
            if (++gameProbes == 2) File.Delete(unknown);
            return UninstallMonitorService.Probe(game,null);
        }, null, true, null, default);
        Wait(gameMonitor);
        Check(gameProbes == 2 && gameMonitor.Result is { Confirmed:true, RetainedSteamAssets:true } && File.Exists(image),
            "Steam monitoring continues while installation files remain and completes when only preserved assets remain");
        File.Delete(unknown);

        var appProgram=new InstalledProgram("Fixture Program","Fixture","1",SizeKb:1,Uninstall:"msiexec.exe /x {2A7C7CAD-510C-4178-B438-DA80A769C97F}");
        var exit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int programProbes = 0;
        var programMonitor = UninstallMonitorService.MonitorAsync(
            () => ++programProbes == 1 ? new(false,true,true,"Waiting for registration")
                : new(true,false,false,"Removed"), exit.Task, false, null, default);
        Check(programProbes == 0 && !programMonitor.IsCompleted, "A tracked uninstaller must exit before verification starts");
        exit.SetResult();
        Wait(programMonitor);
        Check(programProbes == 2 && programMonitor.Result.Confirmed, "Delayed registration updates are rechecked after official process exit");
        using (var cancel = new CancellationTokenSource())
        {
            var stillRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelledMonitor = UninstallMonitorService.MonitorAsync(
                () => throw new InvalidOperationException("Cancelled monitoring must not probe"), stillRunning.Task, false, null, cancel.Token);
            cancel.Cancel();
            try { Wait(cancelledMonitor); } catch (OperationCanceledException) { }
            Check(cancelledMonitor.IsCanceled && !stillRunning.Task.IsCompleted,
                "Cancelling monitoring does not end the tracked uninstaller");
        }
        foreach(bool arabic in new[]{false,true})
        {
            var view=new DeepUninstallerView(arabic,new ActivityLogService(app.Dispatcher),inventory,new SettingsService(Path.Combine(root,"selection-"+arabic)));
            var list=(ListBox)view.FindName("List");
            var gameRow=new ProgramCard(game,arabic);
            var programRow=new ProgramCard(appProgram,arabic);
            Invoke(view,"ApplyDeepSort",new List<ProgramCard>{gameRow,programRow});
            void Toggle(ProgramCard card)=>Wait((Task)Invoke(view,"ApplySelectionGestureAsync",card,ModifierKeys.Control)!);
            void Click(ProgramCard card)=>Wait((Task)Invoke(view,"ApplySelectionGestureAsync",card,ModifierKeys.None)!);
            bool Only(ProgramCard card)=>list.SelectedItems.Count==1&&ReferenceEquals(list.SelectedItem,card);
            Toggle(gameRow);
            Toggle(programRow);
            Check(Only(gameRow),$"{arabic}: Ctrl+Click ignores programs while a game is selected");
            list.SelectedItem=programRow;
            Check(Only(gameRow),$"{arabic}: native selection cannot replace the game with a program");
            Toggle(gameRow);
            Toggle(programRow);
            Wait((Task)Invoke(view,"ApplySelectionGestureAsync",gameRow,ModifierKeys.Shift)!);
            list.SelectedItems.Add(gameRow);
            Wait((Task)Invoke(view,"ApplySelectionKeyAsync",Key.A,ModifierKeys.Control)!);
            Check(Only(programRow),$"{arabic}: a program selection blocks range, checkbox, and select-all from adding games");
            Toggle(programRow);
            Toggle(gameRow);
            Check(Only(gameRow),$"{arabic}: clearing the last program unlocks game selection");
            Click(programRow);
            Check(Only(programRow),$"{arabic}: a plain row click replaces a game with a program");
            Toggle(gameRow);
            Check(Only(programRow),$"{arabic}: Ctrl+Click now uses the new selection type");
            Click(gameRow);
            Check(Only(gameRow),$"{arabic}: a plain row click replaces a program with a game");

            var anotherGame=new ProgramCard(game with { Name="Second fixture game", Identifier="steam:991501503" },arabic);
            Invoke(view,"ApplyDeepSort",new List<ProgramCard>{gameRow,programRow,anotherGame});
            Toggle(anotherGame);
            Click(gameRow);
            Check(Only(gameRow),$"{arabic}: a plain click also reduces a same-type batch to one row");

            view.Measure(new Size(900,650));
            view.Arrange(new Rect(0,0,900,650));
            view.UpdateLayout();
            CheckBox CheckboxFor(ProgramCard card) =>
                FindCheckbox(list.ItemContainerGenerator.ContainerFromItem(card)) ??
                    throw new InvalidOperationException("Fixture row checkbox was not realized.");
            void PressCheckbox(CheckBox checkbox)
            {
                checkbox.ApplyTemplate();
                var box=(Border)checkbox.Template.FindName("Box",checkbox);
                box.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left)
                    { RoutedEvent=Mouse.PreviewMouseDownEvent });
                Pump();
            }
            var gameCheckbox=CheckboxFor(gameRow);
            var programCheckbox=CheckboxFor(programRow);
            PressCheckbox(programCheckbox);
            Check(Only(programRow)&&programCheckbox.IsChecked==true&&gameCheckbox.IsChecked==false,
                $"{arabic}: pressing the program checkbox replaces the game selection");
            PressCheckbox(gameCheckbox);
            Check(Only(gameRow)&&gameCheckbox.IsChecked==true&&programCheckbox.IsChecked==false,
                $"{arabic}: pressing the game checkbox replaces the program selection");
        }
        Check(AppInfo.Version=="1.0.152"&&ReleaseNotesService.LoadCurrent(false)?.Entries.Count==6&&ReleaseNotesService.LoadCurrent(true)?.Entries.Count==6,"All v1.0.152 changes are embedded in both languages");
        Check(((TextBlock)main.FindName("PreviewLabel")).FontSize==11,"Sidebar credit is slightly larger");
        return Finish("Deep150");
    }

    private static CheckBox? FindCheckbox(DependencyObject parent)
    {
        if (parent is CheckBox checkbox) return checkbox;
        for (int i=0; i<VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindCheckbox(VisualTreeHelper.GetChild(parent,i)) is {} child) return child;
        return null;
    }
}

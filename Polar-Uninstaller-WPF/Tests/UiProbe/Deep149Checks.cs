using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PolarUninstallerWpf;
using PolarUninstallerWpf.Services;

internal static partial class Parts146Checks
{
    internal static int RunDeep149()
    {
        Setup("Deep149");
        var log = new ActivityLogService(app.Dispatcher);
        const string command = "msiexec.exe /x {2A7C7CAD-510C-4178-B438-DA80A769C97F}";
        InstalledProgram Item(string name, long size, string uninstall = command) =>
            new(name, "Fixture", "1", SizeKb: size, Uninstall: uninstall);
        var programs = new[] { Item("Fixture A", 1), Item("Fixture B unsupported", 2, ""), Item("Fixture C", 3), Item("Adobe Creative Cloud", 4) with { Publisher = "Adobe" } };
        foreach (bool arabic in new[] { false, true })
        {
            var view = new DeepUninstallerView(arabic, log, new ApplicationInventoryService(_ => Task.FromResult<IReadOnlyList<InstalledProgram>>(programs)),
                new SettingsService(Path.Combine(root, "selection-" + arabic)));
            var list = (ListBox)view.FindName("List");
            var status = (TextBlock)view.FindName("Status");
            var a = new ProgramCard(programs[0], arabic);
            var unsupported = new ProgramCard(programs[1], arabic);
            var c = new ProgramCard(programs[2], arabic);
            var shared = new ProgramCard(programs[3], arabic);
            Invoke(view, "ApplyDeepSort", new List<ProgramCard> { a, unsupported, c, shared });
            bool Selected(params ProgramCard[] expected) => list.SelectedItems.Cast<ProgramCard>().ToHashSet().SetEquals(expected);
            void Gesture(ProgramCard row, ModifierKeys modifiers) => Wait((Task)Invoke(view, "ApplySelectionGestureAsync", row, modifiers)!);
            void KeyGesture(Key key, ModifierKeys modifiers) => Wait((Task)Invoke(view, "ApplySelectionKeyAsync", key, modifiers)!);
            string Plain(string value) => value.Replace("\u2066", "").Replace("\u2069", "");
            string StatusText() => Plain(string.Concat(status.Inlines.OfType<Run>().Select(run=>run.Text)));
            Check(StatusText() == Plain(BrowserText.Get("DeepSelectionTip", arabic)), $"{arabic}: zero selection shows the localized shortcut tip");

            Gesture(a, ModifierKeys.Control);
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (Field(view,"selectionDecision") is null && DateTime.UtcNow < deadline) Pump();
            string explanation = SmartUninstallSelectionService.Evaluate(a.Program, programs).Explanation(arabic);
            Check(Selected(a) && StatusText() == Plain(explanation), $"{arabic}: one selection keeps its existing explanation and cleanup promise");
            Gesture(c, ModifierKeys.Control);
            Check(Selected(a, c) && StatusText() == Plain(BrowserText.Get("DeepSelectionBatch", arabic, 2)), $"{arabic}: Ctrl+Click adds a second row and shows the batch explanation");
            if (arabic) Check(status.Inlines.OfType<Run>().Any(run => run.Text == "2" && run.FlowDirection == FlowDirection.LeftToRight), "Arabic batch count has an isolated LTR run");
            Gesture(a, ModifierKeys.Control);
            Check(Selected(c), $"{arabic}: Ctrl+Click toggles off only the clicked row");
            Gesture(c, ModifierKeys.Shift);
            Check(Selected(a, c) && !list.SelectedItems.Contains(unsupported), $"{arabic}: Shift+Click selects the anchored range and excludes unsupported rows");

            var sort = (ComboBox)view.FindName("SortSelector");
            sort.SelectedIndex = 1;
            Check(Selected(a, c) && ReferenceEquals(list.Items[0], shared), $"{arabic}: selection survives sorting");
            view.SetLanguage(!arabic);
            Check(Selected(a, c) && StatusText() == Plain(BrowserText.Get("DeepSelectionBatch", !arabic, 2)), $"{arabic}: selection and status survive language switching");
            view.SetLanguage(arabic);

            var search = (TextBox)view.FindName("Search");
            search.Text = "Fixture C";
            Check(Selected(c), $"{arabic}: search drops only rows that disappear");
            search.Text = "Fixture";
            Check(Selected(c), $"{arabic}: previously hidden selections do not unexpectedly return");
            KeyGesture(Key.A, ModifierKeys.Control);
            Check(Selected(a, c), $"{arabic}: Ctrl+A respects search and eligibility");
            KeyGesture(Key.D, ModifierKeys.Control);
            Check(Selected() && StatusText() == Plain(BrowserText.Get("DeepSelectionTip", arabic)), $"{arabic}: Ctrl+D clears selection");
            search.Text = "";
            KeyGesture(Key.A, ModifierKeys.Control);
            Check(Selected(a, c, shared) && StatusText().Contains(BrowserText.Get("DeepSharedWarning", arabic)), $"{arabic}: shared component warning appears for the selected batch");
            KeyGesture(Key.Escape, ModifierKeys.None);
            Check(Selected(), $"{arabic}: Escape clears selection");
            KeyGesture(Key.A, ModifierKeys.Control);
            var filter=(ComboBox)Field(view,"kindSelector")!;
            filter.SelectedIndex=2;
            KeyGesture(Key.A, ModifierKeys.Control);
            Check(Selected()&&list.Items.Count==0, $"{arabic}: kind filter drops hidden selections and Ctrl+A honors it");
            filter.SelectedIndex=0;

            if (!arabic)
            {
                Gesture(a, ModifierKeys.Control);
                view.Measure(new Size(900, 650));
                view.Arrange(new Rect(0, 0, 900, 650));
                view.UpdateLayout();
                foreach(var palette in ThemeCatalog.All)
                {
                    CalmThemeBrushes.Apply(view.Resources,palette.Value);
                    Pump();
                    var aRow=(ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(a);
                    var frame=RowFrame(aRow);
                    var expected=(SolidColorBrush)view.Resources["CalmSelectedHoverFill"];
                    var focus=(SolidColorBrush)view.Resources["CalmFocusBrush"];
                    var background=(Color)ColorConverter.ConvertFromString(palette.Value.Background);
                    Check(Selected(a) && frame?.Background is SolidColorBrush actual && actual.Color==expected.Color &&
                        CalmThemeBrushes.Contrast(focus.Color,background)>=3,
                        $"Row selection stays visible when switching to {palette.Key}");
                }
                using var stopButtonToken=new CancellationTokenSource();
                using var activeRemoval=new CancellationTokenSource();
                using var activeMonitor=new CancellationTokenSource();
                Set(view,"batchActive",true);
                Set(view,"batchCancellation",stopButtonToken);
                Set(view,"directCancellation",activeRemoval);
                Set(view,"monitorCancellation",activeMonitor);
                ((Button)Field(view,"cancelRemoval")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(stopButtonToken.IsCancellationRequested&&!activeRemoval.IsCancellationRequested&&!activeMonitor.IsCancellationRequested,
                    "The batch Stop button does not cancel the current removal or its verification");
            }
        }

        var sequence = new List<string>();
        var batch = UninstallBatchService.RunAsync(new[] { 1, 2, 3, 4 }, async (item, _) =>
        {
            sequence.Add($"start:{item}");
            await Task.Yield();
            if (item == 2) throw new IOException("Fixture failure");
            sequence.Add($"verified:{item}");
            return item == 4 ? BatchItemOutcome.Manual : BatchItemOutcome.Succeeded;
        }, default, (index, total) => sequence.Add($"progress:{index}/{total}"), _ => sequence.Add("failed:2"));
        Wait(batch);
        Check(batch.Result is { Succeeded: 2, Failed: 1, Manual: 1, Cancelled: false } && sequence.SequenceEqual(new[]
        {
            "progress:1/4", "start:1", "verified:1", "progress:2/4", "start:2", "failed:2",
            "progress:3/4", "start:3", "verified:3", "progress:4/4", "start:4", "verified:4"
        }), "Batch finishes each verification in order and continues after a failure");

        using var stop = new CancellationTokenSource();
        var stopped = UninstallBatchService.RunAsync(new[] { 1, 2 }, async (_, token) =>
        {
            stop.Cancel();
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            return BatchItemOutcome.Succeeded;
        }, default, stopAfterCurrent: stop.Token);
        Wait(stopped);
        Check(stopped.Result is { Succeeded: 1, Failed: 0, Skipped: 1, Cancelled: true }, "Stop allows current removal to finish and leaves the next one pending");
        return Finish("Deep149");

        static Border? RowFrame(DependencyObject root)
        {
            if (root is Border { Name: "RowFrame" } frame) return frame;
            for (int i=0; i<VisualTreeHelper.GetChildrenCount(root); i++)
                if (RowFrame(VisualTreeHelper.GetChild(root,i)) is {} child) return child;
            return null;
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public partial class DeepUninstallerView
{
    private ProgramCard? selectionAnchor;
    private long gestureRevision;
    private bool applyingSelection;
    private bool SelectionLocked => busy || batchActive || refreshingInventory;

    private static readonly ShortcutHelpEntry[] RemovalShortcuts =
    [
        new("Selection", "Ctrl+Click", "DeepHelpToggle"),
        new("Selection", "Shift+Click", "DeepHelpRange"),
        new("Selection", "Ctrl+A", "DeepHelpSelectAll"),
        new("Selection", "Ctrl+D / Esc", "DeepHelpClear")
    ];

    private void InitializeSelectionGestures()
    {
        var help = new Button { Content = "?", Width = 38, Height = 38, Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("ChromeButton") };
        language.Set(help, ToolTipProperty, lang => BrowserText.Get("DeepHelpButton", lang));
        language.Set(help, System.Windows.Automation.AutomationProperties.NameProperty, lang => BrowserText.Get("DeepHelpButton", lang));
        DockPanel.SetDock(help, Dock.Right);
        ((DockPanel)SortSelector.Parent).Children.Insert(0, help);
        help.Click += (_, _) => ShowSelectionHelp();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F1) return;
            e.Handled = true;
            ShowSelectionHelp();
        };
        List.PreviewMouseLeftButtonDown += SelectionMouseDown;
        List.PreviewKeyDown += SelectionKeyDown;
        ShowSelectionStatus();
    }

    private void ShowSelectionHelp()
    {
        var style = (Style)FindResource("ChromeButton");
        var dialog = new ShortcutHelpWindow(arabic, RemovalShortcuts, BrowserText.Get("DeepHelpScope", arabic), style, (Style)FindResource("DeepKeyboardFocus"))
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }

    private async void SelectionMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (SelectionLocked || e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(List, source) is not ListBoxItem { Content: ProgramCard card } row) return;

        gestureRevision++;
        ModifierKeys modifiers = Keyboard.Modifiers;
        bool canAddToSelection = SelectionAllows(card);
        if (canAddToSelection && ShortcutUi.Ancestor<CheckBox>(source) != null)
        {
            selectionAnchor = card;
            return;
        }

        e.Handled = true;
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0 && !canAddToSelection) return;
        FocusSelectionRow(row);
        await ApplySelectionGestureAsync(card, modifiers);
    }

    private async void SelectionKeyDown(object sender, KeyEventArgs e)
    {
        if (SelectionLocked || ShortcutUi.TextInputFocused(e.OriginalSource as DependencyObject)) return;
        var modifiers = Keyboard.Modifiers;
        gestureRevision++;
        if (IsSelectionKey(e.Key,modifiers))
        {
            e.Handled = true;
            await ApplySelectionKeyAsync(e.Key,modifiers);
        }
        else if ((modifiers & ModifierKeys.Shift) != 0 &&
                 e.Key is Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown && List.Items.Count > 0)
        {
            e.Handled = true;
            var focused = Keyboard.FocusedElement is DependencyObject element ? ItemsControl.ContainerFromElement(List, element) as ListBoxItem : null;
            int current = List.Items.IndexOf(focused?.Content ?? List.SelectedItem);
            int page = Math.Max(1, (int)(List.ActualHeight / 66));
            int next = e.Key switch
            {
                Key.Home => 0, Key.End => List.Items.Count - 1,
                Key.Up => current - 1, Key.Down => current + 1,
                Key.PageUp => current - page, _ => current + page
            };
            var card = (ProgramCard)List.Items[Math.Clamp(next, 0, List.Items.Count - 1)];
            selectionAnchor ??= focused?.Content as ProgramCard ?? List.SelectedItem as ProgramCard ?? card;
            List.ScrollIntoView(card);
            List.UpdateLayout();
            if (List.ItemContainerGenerator.ContainerFromItem(card) is ListBoxItem row) FocusSelectionRow(row);
            await ApplySelectionGestureAsync(card, modifiers);
        }
    }

    private static bool IsSelectionKey(Key key,ModifierKeys modifiers) =>
        key==Key.Escape || (modifiers==ModifierKeys.Control && key is Key.A or Key.D);

    private Task ApplySelectionKeyAsync(Key key,ModifierKeys modifiers)
    {
        if (!IsSelectionKey(key,modifiers)) return Task.CompletedTask;
        if (key==Key.A) return SelectEligibleAsync(List.Items.OfType<ProgramCard>().ToArray(),append:false);
        ClearProgramSelection();
        return Task.CompletedTask;
    }

    private void FocusSelectionRow(ListBoxItem row)
    {
        var selected = List.SelectedItems.Cast<ProgramCard>().ToArray();
        applyingSelection = true;
        try
        {
            row.Focus();
            if (List.SelectedItems.Count == selected.Length && selected.All(List.SelectedItems.Contains)) return;
            List.UnselectAll();
            foreach (var card in selected) List.SelectedItems.Add(card);
        }
        finally { applyingSelection = false; }
    }

    private Task ApplySelectionGestureAsync(ProgramCard card, ModifierKeys modifiers)
    {
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
        {
            selectionAnchor = card;
            if (List.SelectedItems.Count == 1 && ReferenceEquals(List.SelectedItem, card)) return Task.CompletedTask;
            applyingSelection = true;
            try
            {
                List.UnselectAll();
                List.SelectedItems.Add(card);
            }
            finally { applyingSelection = false; }
            RefreshSelectionActions(showStatus: true);
            return Task.CompletedTask;
        }

        if (!SelectionAllows(card)) return Task.CompletedTask;
        if ((modifiers & ModifierKeys.Shift) == 0)
        {
            selectionAnchor = card;
            if (List.SelectedItems.Contains(card)) List.SelectedItems.Remove(card);
            else List.SelectedItems.Add(card);
            return Task.CompletedTask;
        }

        var visible = List.Items.OfType<ProgramCard>().ToArray();
        int end = Array.IndexOf(visible, card);
        if (end < 0) return Task.CompletedTask;
        int start = selectionAnchor is null ? -1 : Array.IndexOf(visible, selectionAnchor);
        if (start < 0) { selectionAnchor = card; start = end; }
        int first = Math.Min(start, end);
        return SelectEligibleAsync(visible[first..(Math.Max(start, end) + 1)], (modifiers & ModifierKeys.Control) != 0);
    }

    private async Task SelectEligibleAsync(ProgramCard[] candidates, bool append)
    {
        long revision = ++gestureRevision;
        bool? selectedKind = List.SelectedItems.OfType<ProgramCard>().Select(card => (bool?)IsGame(card.Program)).FirstOrDefault();
        if (selectedKind is bool kind)
            candidates = candidates.Where(card => IsGame(card.Program) == kind).ToArray();
        var records = (List.ItemsSource as IEnumerable<ProgramCard> ?? candidates).Select(card => card.Program).ToArray();
        try
        {
            var eligible = await Task.Run(() => candidates.Where(card =>
                SmartUninstallSelectionService.Evaluate(card.Program, records).Method != UninstallMethod.Unavailable).ToArray(), cancellation.Token);
            if (revision != gestureRevision || SelectionLocked || cancellation.IsCancellationRequested) return;
            if (eligible.Length == 0 && selectedKind.HasValue) return;
            if (eligible.Length > 0 && !selectedKind.HasValue) selectedKind = IsGame(eligible[0].Program);
            applyingSelection = true;
            try
            {
                if (!append) List.UnselectAll();
                foreach (var card in eligible)
                    if (IsGame(card.Program) == selectedKind && List.Items.Contains(card) && !List.SelectedItems.Contains(card)) List.SelectedItems.Add(card);
            }
            finally { applyingSelection = false; }
            RefreshSelectionActions(showStatus: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (revision != gestureRevision) return;
            CrashDiagnostics.Record("Deep selection", error);
            SetStatus("Could not evaluate selection. Refresh the list.", "تعذر التحقق من التحديد. حدّث القائمة.");
        }
    }

    private bool SelectionAllows(ProgramCard card) =>
        List.SelectedItems.OfType<ProgramCard>().FirstOrDefault() is not {} first || IsGame(first.Program) == IsGame(card.Program);

    // Native keyboard and accessibility selection can also change the ListBox directly.
    private bool RejectIncompatibleSelection(SelectionChangedEventArgs change)
    {
        if (change.AddedItems.Count == 0) return false;
        var previous = List.SelectedItems.OfType<ProgramCard>().Where(card => !change.AddedItems.Contains(card))
            .Concat(change.RemovedItems.OfType<ProgramCard>().Where(List.Items.Contains)).ToArray();
        var first = previous.FirstOrDefault() ?? change.AddedItems.OfType<ProgramCard>().FirstOrDefault();
        if (first is null) return false;
        bool games = IsGame(first.Program);
        var rejected = List.SelectedItems.OfType<ProgramCard>().Where(card => IsGame(card.Program) != games).ToArray();
        if (rejected.Length == 0) return false;

        applyingSelection = true;
        try
        {
            foreach (var card in rejected) List.SelectedItems.Remove(card);
            foreach (var card in previous)
                if (!List.SelectedItems.Contains(card)) List.SelectedItems.Add(card);
        }
        finally { applyingSelection = false; }
        return previous.Length == List.SelectedItems.Count && previous.All(List.SelectedItems.Contains);
    }

    private void RefreshSelectionActions(bool showStatus)
    {
        SelectionChanged(List,showStatus
            ? new SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                Array.Empty<ProgramCard>(),List.SelectedItems.Cast<ProgramCard>().ToArray())
            : null!);
    }

    private void ClearProgramSelection()
    {
        gestureRevision++;
        selectionAnchor = null;
        List.UnselectAll();
    }

    private void ShowSelectionStatus()
    {
        int count = List.SelectedItems.Count;
        if (count == 0) SetStatus(lang => BrowserText.Get("DeepSelectionTip", lang));
        else if (count > 1)
        {
            bool shared = HasSharedSelection(List.SelectedItems.Cast<ProgramCard>());
            SetStatus(lang => BrowserText.Get("DeepSelectionBatch", lang, count) +
                (shared ? " " + BrowserText.Get("DeepSharedWarning", lang) : ""));
        }
    }
}

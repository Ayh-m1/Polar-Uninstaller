using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using PolarUninstallerWpf.Services;

namespace PolarUninstallerWpf;

public partial class DeepUninstallerView : UserControl
{
    private bool arabic;
    private readonly LocalizedPresentation language = new();
    private bool batchActive;
    private CancellationTokenSource? batchCancellation;
    private Task? monitorTask;
    private BatchItemOutcome operationOutcome=BatchItemOutcome.Failed;
    private readonly ActivityLogService activity;
    private readonly ApplicationInventoryService inventory;
    private readonly SettingsService settingsService;
    private CancellationTokenSource cancellation = new();
    private CancellationTokenSource sizeCancellation = new();
    private string deepSort = "name";
    private ICollectionView? view;
    private bool busy;
    public bool IsOperationActive => busy||batchActive;
    private static readonly SemaphoreSlim Operation = new(1, 1);
    private CancellationTokenSource? directCancellation;
    private CancellationTokenSource? monitorCancellation;
    private Task? officialProcessExit;
    private readonly Button cancelRemoval = new();
    private readonly Button advancedDeepRemoval = new();
    private ProgramCard? advancedCard;
    private RemovalPlan? advancedPlan;
    private RemovalPlan? officialPlan;
    private HashSet<string>? advancedScope;
    private bool refreshingInventory;private int scanGeneration;
    private UninstallReport? lastDirectReport;
    private readonly UninstallHistoryService history = new();
    private string L(string en, string ar) => BidiText.Localize(arabic,en,ar);

    public DeepUninstallerView(bool arabic, ActivityLogService activity, ApplicationInventoryService inventory, SettingsService? settingsService=null)
    {
        this.arabic = arabic; this.activity = activity; this.inventory = inventory;
        this.settingsService=settingsService??new SettingsService();
        InitializeComponent();UiPerformanceProbe.Attach(this);
        language.Refresh(arabic);
        FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        deepSort = this.settingsService.Load().DeepUninstallerSort ?? "name";
        ConfigureSortSelector();
        InitializeSelectionControls();
        language.Set(Heading,TextBlock.TextProperty, lang => BidiText.Localize(lang,"Deep Uninstaller", "الإزالة العميقة")); language.Set(RefreshButton,ContentControl.ContentProperty, lang => BidiText.Localize(lang,"Refresh", "تحديث"));
        language.Set(Search,ToolTipProperty, lang => BidiText.Localize(lang,"Search installed applications", "البحث في البرامج المثبتة"));
        language.Set(Games,ContentControl.ContentProperty, lang => BidiText.Localize(lang,"Games Uninstall", "إزالة الألعاب")); language.Set(Deep,ContentControl.ContentProperty, lang => BidiText.Localize(lang,"Deep Uninstall", "الإزالة العميقة"));
        language.Set(SteamDb,ContentControl.ContentProperty, lang => BidiText.Localize(lang,"Open in SteamDB", "فتح في SteamDB"));
        Deep.Click += DeepClick;
        cancelRemoval.Style=(Style)Application.Current.Resources["ChromeButton"];SetRemovalControl(false);
        cancelRemoval.Click+=(_,_)=>{if(batchActive){batchCancellation?.Cancel();SetRemovalControl(true);return;}if(monitorCancellation!=null)CancelMonitoring();directCancellation?.Cancel();};
        DockPanel.SetDock(cancelRemoval,Dock.Right);((DockPanel)Deep.Parent).Children.Insert(0,cancelRemoval);
language.Set(advancedDeepRemoval,ContentControl.ContentProperty, lang => BidiText.Localize(lang,"Advanced Deep Removal","الإزالة العميقة المتقدمة"));advancedDeepRemoval.Style=Deep.Style;advancedDeepRemoval.Margin=new Thickness(0,0,8,0);advancedDeepRemoval.Visibility=Visibility.Collapsed;
advancedDeepRemoval.Click+=async(_,_)=>await RunRemovalActionAsync(AdvancedDeepRemovalAsync);
DockPanel.SetDock(advancedDeepRemoval,Dock.Right);((DockPanel)Deep.Parent).Children.Insert(0,advancedDeepRemoval);
        Loaded += async (_, _) => {ResetLifetime();await Refresh();};
        Unloaded += (_, _) => { cancellation.Cancel(); sizeCancellation.Cancel(); monitorCancellation?.Cancel(); directCancellation?.Cancel(); };
    }
    public void SetLanguage(bool value)
    {
        arabic=value;
        FlowDirection=value?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        language.Refresh(value);
        ConfigureSortSelector();
        if(kindSelector!=null)
        {
            // Replace the display labels without changing the selected filter.
            int selected = kindFilter switch { "programs" => 1, "games" => 2, _ => 0 };
            updatingKindLanguage = true;
            try
            {
                kindSelector.Items.Clear();
                foreach (string label in new[]{ L("All","الكل"), L("Programs","البرامج"), L("Games","الألعاب") })
                    kindSelector.Items.Add(label);
                kindSelector.SelectedIndex = selected;
                kindSelector.ToolTip = L("Filter applications","تصفية البرامج");
            }
            finally { updatingKindLanguage = false; }
        }
        if (List.ItemsSource is not IEnumerable<ProgramCard> cards) return;
        foreach (var card in cards) card.SetLanguage(value);

    }

    private bool updatingKindLanguage;
    private void SetStatus(string en,string ar) => SetStatus(lang=>BidiText.Localize(lang,en,ar));

    private void SetStatus(Func<bool,string> text)
    {
        var progress=batchProgress;
        language.Set(Status,TextBlock.TextProperty,lang=>
        {
            string message=text(lang);
            if(progress is not {} item) return message;
            string prefix=BrowserText.Get("DeepBatchProgress",lang,item.Index,item.Total,item.Name);
            if(item.Shared) prefix+=" "+BrowserText.Get("DeepSharedWarning",lang);
            return message.Length==0 ? prefix : prefix+" · "+message;
        });
    }
    private void ResetLifetime(){if(cancellation.IsCancellationRequested){cancellation.Dispose();cancellation=new();scanGeneration++;refreshingInventory=false;}}
    private async void RefreshClick(object sender, RoutedEventArgs e) => await Refresh();
    private bool FilterCard(object value) => value is ProgramCard card && (kindFilter=="all"||kindFilter=="games"&&IsGame(card.Program)||kindFilter=="programs"&&!IsGame(card.Program)) && (card.Name.Contains(Search.Text, StringComparison.CurrentCultureIgnoreCase) || card.Details.Contains(Search.Text, StringComparison.CurrentCultureIgnoreCase));
    private async Task Refresh()
    {
        if(refreshingInventory||busy||batchActive)return;
        refreshingInventory = true;int request=++scanGeneration;var scanToken=cancellation.Token;
        sizeCancellation.Cancel();
        sizeCancellation = new CancellationTokenSource();
        RefreshButton.IsEnabled = false; SetStatus("Scanning…","جارٍ الفحص…"); activity.Write("INFO",L("Application and launcher scan started.","بدأ فحص البرامج والألعاب."));
        try
        {
            var previous = await Task.Run(()=>InventorySnapshotService.Load(),scanToken);
            var snapshot = await inventory.RefreshAsync(scanToken);
            if (scanToken.IsCancellationRequested || request!=scanGeneration || !inventory.IsCurrent(snapshot)) return;

            // Only compare inventories when the new scan is complete enough to
            // be trusted. A failed registry/launcher read must never look like a
            // mass uninstallation.
            if (snapshot.IsComplete && previous.Count > 0)
                await ProcessExternalRemovalsAsync(previous, snapshot.Programs);
            else if (previous.Count > 0)
                activity.Write("WARNING", "Application scan was incomplete; external removals were not evaluated.");

            if(scanToken.IsCancellationRequested||request!=scanGeneration)return;
            var selected = List.SelectedItems.Cast<ProgramCard>().Select(c=>c.Program).ToArray();
            // One INFO summary per scan plus one line per failed icon; lazy rows are
            // counted as pending, never as a permanent failure.
            IconDiagnostics.Begin(snapshot.Programs, activity);
            var result = snapshot.Programs.Select(item => new ProgramCard(item, arabic)).ToList();
            List.ItemsSource = result; view = CollectionViewSource.GetDefaultView(result);
            view.Filter = FilterCard;
            ApplyDeepSort(result);
            // Asynchronously measure sizes for items without a reported size.
            sizeCancellation.Cancel();
            sizeCancellation = new CancellationTokenSource();
            var sizeToken = sizeCancellation.Token;
            foreach (var card in result)
            {
                if (card.Program.SizeKb.HasValue && card.Program.SizeKb.Value > 0) continue;
                string key = ApplicationSizeService.NormalizeKey(card.Program.Location);
                if (key.Length == 0) continue;
                if (ApplicationSizeService.TryGetCached(card.Program.Location, out long? cached))
                {
                    card.UpdateSize(cached.HasValue ? cached.Value / 1024 : null);
                    continue;
                }
                card.SetCalculating();
                var capturedCard = card;
                var capturedLocation = card.Program.Location;
                _ = Task.Run(async () =>
                {
                    long? measured = null;
                    try { if(Directory.Exists(capturedLocation))measured = await ApplicationSizeService.MeasureAsync(capturedLocation, sizeToken); }
                    catch (OperationCanceledException) { return; }
                    if (sizeToken.IsCancellationRequested) return;
                    if(Dispatcher.HasShutdownStarted)return;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (sizeToken.IsCancellationRequested) return;
                        capturedCard.UpdateSize(measured.HasValue ? measured.Value / 1024 : null);
                    });
                }, sizeToken);
            }
            applyingSelection=true;
            try
            {
                foreach(var card in List.Items.OfType<ProgramCard>())
                    if(selected.Any(p=>SameSelection(p,card.Program)))List.SelectedItems.Add(card);
            }
            finally { applyingSelection=false; }
            // The count is the authoritative unfiltered inventory total. The
            // search box only filters the visible collection view below.
            language.Set(CountText,TextBlock.TextProperty,lang=>snapshot.IsComplete ? BidiText.Localize(lang,$"{snapshot.Count} items",$"العناصر: {snapshot.Count}") : "—");
            if (snapshot.IsComplete) ShowSelectionStatus();
            else SetStatus("Scan incomplete; showing partial results.", "الفحص غير مكتمل؛ تُعرض نتائج جزئية.");
            activity.Write(snapshot.IsComplete ? "INFO" : "WARNING",L($"Application scan completed: {snapshot.Count} entries.","اكتمل فحص البرامج. العناصر: "+DisplayFormat.Isolate(DisplayFormat.Number(snapshot.Count))));

            // Preserve the previous snapshot when this scan was incomplete so the
            // next successful scan can still evaluate removals correctly.
            if (snapshot.IsComplete) await Task.Run(()=>InventorySnapshotService.Save(null,snapshot.Programs));
        }
        catch (OperationCanceledException) { activity.Write("INFO", "Application scan cancelled."); }
        catch (Exception ex) { SetStatus("Could not load applications. Refresh and check access permissions.","تعذر تحميل البرامج. حدّث القائمة وتحقق من صلاحيات الوصول."); activity.Write("ERROR",Status.Text);CrashDiagnostics.Record("Inventory refresh",ex); }
        finally { if(request==scanGeneration){refreshingInventory=false; UpdateActions();} }
    }

    /// <summary>
    /// Confirms external removals using persisted absence state. A removal is
    /// recorded only once, and only after it has been verified either by a
    /// missing installation folder or by a second successful scan.
    /// </summary>
    private async Task ProcessExternalRemovalsAsync(IReadOnlyList<InstalledProgram> previous, IReadOnlyList<InstalledProgram> current)
    {
        var pending = InventorySnapshotService.LoadPending()
            .GroupBy(entry => ExternalRemovalDetector.IdentityKey(entry.Program), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recentHistory = (await history.LoadAsync())
            .Where(entry => (DateTimeOffset.Now - entry.RemovedAt).TotalHours <= 24)
            .ToArray();

        foreach (var item in previous)
        {
            if (current.Any(candidate => ExternalRemovalDetector.StillPresent(item, candidate))) continue;
            string key = ExternalRemovalDetector.IdentityKey(item);
            handled.Add(key);
            if (recentHistory.Any(entry => UninstallHistoryService.Matches(entry, item)))
            {
                pending.Remove(key);
                continue;
            }
            pending.TryGetValue(key, out var existing);
            int prior = existing?.AbsenceCount ?? 0;
            var decision = ExternalRemovalDetector.Evaluate(item, current, prior);
            if (decision == ExternalRemovalDecision.Confirmed)
            {
                pending.Remove(key);
                await RecordExternalRemovalAsync(item);
            }
            else if (decision == ExternalRemovalDecision.Pending)
            {
                pending[key] = new PendingRemoval(item, existing?.FirstSeen ?? DateTimeOffset.Now, prior + 1);
            }
            else
            {
                pending.Remove(key);
            }
        }

        // Pending entries are no longer in the snapshot (it is replaced after
        // every successful scan), so they must be evaluated independently.
        foreach (string key in pending.Keys.ToArray())
        {
            if (handled.Contains(key)) continue;
            var entry = pending[key];
            if (current.Any(candidate => ExternalRemovalDetector.StillPresent(entry.Program, candidate)))
            {
                pending.Remove(key);
                continue;
            }
            int prior = entry.AbsenceCount;
            var decision = ExternalRemovalDetector.Evaluate(entry.Program, current, prior);
            if (decision == ExternalRemovalDecision.Confirmed)
            {
                pending.Remove(key);
                await RecordExternalRemovalAsync(entry.Program);
            }
            else if (decision == ExternalRemovalDecision.Pending)
            {
                pending[key] = entry with { AbsenceCount = prior + 1 };
            }
            else
            {
                pending.Remove(key);
            }
        }

        InventorySnapshotService.SavePending(null, pending.Values);
    }

    private async Task RecordExternalRemovalAsync(InstalledProgram item)
    {
        var external = item with { Source = "External" };
        var capture = await history.CaptureAsync(external);
        if (await history.RecordAsync(capture))
            activity.Write("INFO", "Recorded external removal: " + item.Name);
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) => RefreshFilter();

    private void ConfigureSortSelector()
    {
        updatingSortLanguage=true;
        SortSelector.Items.Clear();
        SortSelector.Items.Add(L("Name (A–Z)", "الاسم (أ–ي)"));
        SortSelector.Items.Add(L("Largest First", "الأكبر حجمًا أولًا"));
        SortSelector.Items.Add(L("Smallest First", "الأصغر حجمًا أولًا"));
        SortSelector.SelectedIndex = deepSort switch { "sizeDesc" => 1, "sizeAsc" => 2, _ => 0 };
        updatingSortLanguage=false;
    }

    private bool updatingSortLanguage;
    private void SortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingSortLanguage || SortSelector.SelectedIndex < 0) return;
        deepSort = SortSelector.SelectedIndex switch { 1 => "sizeDesc", 2 => "sizeAsc", _ => "name" };
        try
        {
            var current = settingsService.Load();
            settingsService.Save(current with{DeepUninstallerSort=deepSort});
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        if (List.ItemsSource is IEnumerable<ProgramCard> cards) ApplyDeepSort(cards.ToList());
    }

    private void ApplyDeepSort(List<ProgramCard> cards)
    {
        gestureRevision++;
        cards.Sort((a, b) =>
        {
            if (deepSort == "sizeDesc" || deepSort == "sizeAsc")
            {
                bool aKnown = a.MeasuredSizeKb.HasValue;
                bool bKnown = b.MeasuredSizeKb.HasValue;
                if (!aKnown && !bKnown) return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                if (!aKnown) return 1;
                if (!bKnown) return -1;
                int result = a.MeasuredSizeKb!.Value.CompareTo(b.MeasuredSizeKb!.Value);
                if (deepSort == "sizeDesc") result = -result;
                return result != 0 ? result : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            }
            return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });
        var selected=List.SelectedItems.Cast<ProgramCard>().Select(c=>c.Program).ToArray();
        applyingSelection=true;
        try
        {
            List.ItemsSource = cards;
            view = CollectionViewSource.GetDefaultView(cards);
            view.Filter = FilterCard;
            foreach(var card in List.Items.OfType<ProgramCard>())
                if(selected.Any(p=>SameSelection(p,card.Program)))List.SelectedItems.Add(card);
            if(selectionAnchor is not null&&!view.Contains(selectionAnchor))selectionAnchor=null;
        }
        finally { applyingSelection=false; }
        SelectionChanged(List,null!);
    }

    private bool IsGame(InstalledProgram program) =>
        GameClassificationService.IsGame(program);
    private bool IsVerifiedSteamGame(InstalledProgram program)
    {
        if (!program.Identifier.StartsWith("steam:", StringComparison.OrdinalIgnoreCase)) return false;
        string appId = program.Identifier["steam:".Length..];
        return appId.Length > 0 && appId.All(char.IsAsciiDigit) && appId != "0";
    }

    private long selectionRevision;
    private SmartUninstallDecision? selectionDecision;
    private async void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (applyingSelection || (e is not null && RejectIncompatibleSelection(e))) return;
        long revision=++selectionRevision;
        var card=List.SelectedItem as ProgramCard;
        bool advancedRelevant=advancedCard!=null&&advancedPlan!=null&&
            (card==null||UninstallVerificationService.Matches(card.Program,advancedCard.Program));
        advancedDeepRemoval.Visibility=advancedRelevant?Visibility.Visible:Visibility.Collapsed;
        advancedDeepRemoval.IsEnabled=advancedRelevant&&!busy&&monitorCancellation==null;
        
        Games.IsEnabled=Deep.IsEnabled=false;
        ToolTipService.SetShowOnDisabled(Games,true);ToolTipService.SetShowOnDisabled(Deep,true);
        SelectedDetails.Text="";SteamDb.Visibility=Visibility.Collapsed;selectionDecision=null;
        if(List.SelectedItems.Count==1)
            language.Set(SelectedDetails,TextBlock.TextProperty,lang=>BidiText.Localize(lang,"Selected: ","المحدد: ")+DisplayFormat.Isolate("1"));
        else language.Set(SelectedDetails,TextBlock.TextProperty,_=>"");
        if(e is not null&&!SelectionLocked) ShowSelectionStatus();
        if(card==null||busy||batchActive||refreshingInventory||monitorCancellation!=null)return;
        if(List.SelectedItems.Count>1){await EvaluateBatchActionsAsync(revision);return;}
        var records=(List.ItemsSource as IEnumerable<ProgramCard>??Array.Empty<ProgramCard>()).Select(c=>c.Program).ToArray();
        try
        {
            var decision=await Task.Run(()=>SmartUninstallSelectionService.Evaluate(card.Program,records),cancellation.Token);
            if(revision!=selectionRevision||busy||batchActive||refreshingInventory||cancellation.IsCancellationRequested||!ReferenceEquals(List.SelectedItem,card))return;
            selectionDecision=decision;
            Games.IsEnabled=decision.GamesEnabled&&!advancedRelevant;
            Deep.IsEnabled=decision.DeepEnabled&&!advancedRelevant;

            Games.ToolTip=decision.Tooltip(UninstallMethod.Games,arabic);
            Deep.ToolTip=decision.Tooltip(UninstallMethod.Deep,arabic);

            if(e is not null)SetStatus(decision.Explanation);
            if(IsVerifiedSteamGame(card.Program))
            {
                language.Set(SelectedDetails,TextBlock.TextProperty,lang=>BidiText.Localize(lang,"Steam App ID: ","معرف Steam App ID: ")+DisplayFormat.Isolate(card.Program.Identifier[6..]));SteamDb.Visibility=Visibility.Visible;
            }
        }
        catch(OperationCanceledException){ }
        catch(Exception ex)
        {
            if(revision!=selectionRevision)return;
            SetStatus("Removal capabilities could not be verified. Refresh the inventory.","تعذر التحقق من طرق الإزالة. حدّث قائمة البرامج.");
            activity.Write("WARNING","Smart uninstall selection failed: "+ex.GetType().Name);
            CrashDiagnostics.Record("Removal selection",ex);
        }
    }
    private async void DeepClick(object sender,RoutedEventArgs e)
    {
        if(!Deep.IsEnabled)return;
        await UninstallSelectionAsync(true);
    }
    private async Task AdvancedDeepRemovalAsync()
    {
        if(busy||monitorCancellation!=null||advancedCard is null||advancedPlan is null)return;
        if(!Operation.Wait(0))
        {SetStatus("Another removal or its official uninstaller is still running. Close or finish it before recovery.","ما زالت إزالة أخرى أو مزيلها الرسمي قيد التشغيل. أكملها أو أغلقها قبل الاستعادة.");activity.Write("INFO",Status.Text);return;}
        if(List.SelectedItem is ProgramCard selected&&!UninstallVerificationService.Matches(selected.Program,advancedCard.Program))
        {Operation.Release();return;}
        await RunDirectPassAsync(advancedCard, advancedPlan);
    }

    private async Task RunDirectPassAsync(ProgramCard card,RemovalPlan? retry,RemovalPlan? preparedPlan=null,IReadOnlyList<InstalledProgram>? preparedInventory=null,bool failedOfficialFallback=false)
    {
        busy=true; UpdateActions();
        using var cts=CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);directCancellation=cts;
        SetRemovalControl(true);
        RemovalPlan? plan=retry??preparedPlan;
        try
        {
            var current=preparedInventory??await ReadRemovalInventoryAsync(cts.Token);
            if(retry==null)
            {
                var decision=await Task.Run(()=>SmartUninstallSelectionService.Evaluate(card.Program,current),cts.Token);
                if(!decision.DeepEnabled||(decision.UsesOfficialUninstaller&&!failedOfficialFallback)){SetStatus(decision.Explanation);return;}
            }
            var engine=new RemovalEngine();
            if(plan==null)
            {
                // Planning is read-only. Capture every identity before the first deletion.
                SetStatus("Preparing verified removal plan…","جارٍ تجهيز خطة الإزالة المتحقق منها…");
                var snapshot=await new SharedLeftoverScanner().CaptureAsync(card.Program,cts.Token);
                plan=await engine.PlanAsync(snapshot,current,true,cts.Token);

            }
            HashSet<string>? authorizedCleanup=null;
            if(retry!=null)
            {
                authorizedCleanup=new HashSet<string>(advancedScope??new(),StringComparer.OrdinalIgnoreCase);
                var latest=await engine.InspectAsync(plan,current,cts.Token);
                var added=latest.Where(c=>!authorizedCleanup.Contains(c.Category+"|"+c.Path)).ToArray();
                if(added.Length>0)
                {
                    string message=L("New verified cleanup targets were discovered outside the previous cleanup scope:\n\n","اكتُشفت عناصر تنظيف متحقق منها خارج النطاق السابق:\n\n")+
                        string.Join("\n",added.Select(c=>c.Category+": "+c.Path))+L("\n\nAuthorize these additional targets?","\n\nالسماح بتنظيف هذه العناصر الإضافية؟");
                    if(MessageBox.Show(Window.GetWindow(this),message,"Polar Uninstaller",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
                    authorizedCleanup.UnionWith(added.Select(c=>c.Category+"|"+c.Path));
                }
            }
            var capture=await history.CaptureAsync(card.Program,token:cts.Token);
            if(retry!=null&&plan.Root!=null)
            {
                var running=await Task.Run(()=>DirectRemovalService.RunningProcesses(plan.Root),cts.Token);
                if(running.Count>0)
                {
                    string message=L("These application-owned processes may lock the remaining files:\n","قد تمنع عمليات البرنامج التالية حذف الملفات المتبقية:\n")+
                        string.Join("\n",running)+L("\n\nClose these specific processes?","\n\nإغلاق هذه العمليات المحددة؟");
                    if(MessageBox.Show(Window.GetWindow(this),message,"Polar Uninstaller",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes)
                        foreach(string entry in running)
                        {
                            int start=entry.LastIndexOf('(');
                            if(start<0||!int.TryParse(entry[(start+1)..].TrimEnd(')'),out int pid))continue;
                            try
                            {
                                using var process=Process.GetProcessById(pid);
                                // Re-check the live image path; a stale PID/name is not authority.
                                string? image=process.MainModule?.FileName;
                                if(image!=null&&image.StartsWith(plan.Root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))
                                {process.Kill();await process.WaitForExitAsync(cts.Token);}
                            }
                            catch(Exception ex)when(ex is Win32Exception or InvalidOperationException or ArgumentException)
                            {activity.Write("WARNING",L("Could not close an application process: ","تعذر إغلاق إحدى عمليات البرنامج: ")+entry);}
                        }
                }
            }
            SetRemovalControl(true);
            SetStatus("Removing verified resources…","جارٍ إزالة الموارد المتحقق منها…");
            activity.Write("INFO",L("Removal plan: ","خطة الإزالة: ")+card.Name+" · "+card.Program.Publisher+" · "+(plan.Root??card.Program.Location));
            var progress=new Progress<string>(n=>SetStatus(lang => BidiText.Localize(lang,"Removed files: ","الملفات المحذوفة: ") + DisplayFormat.Directional(n,lang)));
            var pass=await engine.ExecuteAsync(plan,current,progress,cts.Token,authorizedCleanup);
            await Task.Run(()=>
            {
                foreach(var message in pass.Cleanup.Messages)
                    activity.Write(message.StartsWith("Removed",StringComparison.Ordinal)?"INFO":"WARNING",LocalizeRemovalMessage(message));
            });
            await FinishDirectPassAsync(card,pass,capture,cts.Token);
        }
        catch(OperationCanceledException)
        {
            SetStatus("Removal cancelled. Completed deletions cannot be undone.","أُلغيت الإزالة. لا يمكن التراجع عن العناصر المحذوفة.");
            activity.Write("WARNING",Status.Text);
            if(plan!=null)await RetainEligibleContextAsync(card,plan);
        }
        catch(Exception ex)when(RemovalEngine.IsItemError(ex)||ex is Win32Exception)
        {
            SetStatus("Removal could not be completed. Review the Activity Log.","تعذر إكمال الإزالة. راجع سجل النشاط.");
            activity.Write("ERROR",LocalizeRemovalMessage(RemovalEngine.Reason(ex)));
            if(plan!=null)await RetainEligibleContextAsync(card,plan);
        }
        finally
        {
            directCancellation=null;SetRemovalControl(false);busy=false;Operation.Release();
            UpdateActions();
        }
    }

    private async Task<IReadOnlyList<InstalledProgram>> ReadRemovalInventoryAsync(CancellationToken token)
    {
        var windows=await new WindowsInventoryService().GetInstalledAsync(token);
        var games=await new LauncherInventoryService().GetInstalledAsync(token);
        return InventoryMerger.Merge(windows.Concat(games));
    }

    private async Task RetainEligibleContextAsync(ProgramCard card,RemovalPlan plan)
    {
        try
        {
            var current=await ReadRemovalInventoryAsync(CancellationToken.None);
            var candidates=await new RemovalEngine().InspectAsync(plan,current,CancellationToken.None);
            SetAdvancedContext(card,plan,candidates.Count>0,candidates);
        }
        catch(Exception ex)when(RemovalEngine.IsItemError(ex))
        {activity.Write("WARNING",L("Remaining targets could not be verified; no advanced cleanup was enabled.","تعذر التحقق من العناصر المتبقية؛ لم تُفعّل الإزالة المتقدمة."));}
    }

    private void SetAdvancedContext(ProgramCard card,RemovalPlan plan,bool eligible,IReadOnlyList<LeftoverCandidate>? candidates=null)
    {
        advancedCard=eligible?card:null;advancedPlan=eligible?plan:null;
        advancedScope=eligible?(candidates??Array.Empty<LeftoverCandidate>()).Select(c=>c.Category+"|"+c.Path).ToHashSet(StringComparer.OrdinalIgnoreCase):null;
        if(eligible&&advancedScope!=null)advancedScope.UnionWith(plan.Directories.Select(path=>"Folder|"+path));
        if(eligible&&advancedScope!=null)advancedScope.UnionWith(plan.Files.Select(file=>"File|"+file.Path));
        advancedDeepRemoval.ToolTip=eligible?card.Name:null;
        UpdateActions();
    }

    private async Task FinishDirectPassAsync(ProgramCard card,RemovalPass pass,UninstallHistoryCapture? capture,CancellationToken token)
    {
        var current=await ReadRemovalInventoryAsync(token);
        var verification=UninstallVerificationService.Assess(card.Program,current);
        bool executable=UninstallVerificationService.AnyExecutablePresent(pass.Plan.Snapshot.Executables);
        var probe=await Task.Run(()=>UninstallMonitorService.Probe(card.Program,pass.Plan.Snapshot),token);
        var report=UninstallReportService.FromProbe(card.Program,probe,pass.Cleanup,
            "Unified verified direct removal",verification.Incomplete||pass.Plan.Notes.Count>0);
        lastDirectReport=report;
        WriteReport(report);
        // The final inventory was already read above. Preserve the current
        // rows/context; RemoveCard publishes a verified removal and refreshes
        // the shared inventory without rebuilding this page a second time.
        bool gone=!verification.Incomplete&&probe.Confirmed&&!executable;
        if(gone)
        {
            operationOutcome=BatchItemOutcome.Succeeded;
            RemoveCard(card,pass.Plan.Snapshot.Executables);
            if(capture!=null)await RecordHistoryAsync(card,capture);
        }
        var remaining=await new RemovalEngine().InspectAsync(pass.Plan,current,token);
        SetAdvancedContext(card,pass.Plan,remaining.Count>0,remaining);
        SetStatus(report.Summary);
    }

    private string LocalizeRemovalMessage(string message)=>!arabic?message:
        "تفاصيل الإزالة: "+message.Replace("Removed file:","حُذف الملف:").Replace("Removed folder:","حُذف المجلد:")
        .Replace("Removed shortcut permanently:","حُذف الاختصار المتحقق منه نهائيًا:")
        .Replace("Failed file:","تعذر حذف الملف:").Replace("Retained folder:","احتُفظ بالمجلد:")
        .Replace("Skipped changed file:","تُرك ملف تغيّر بعد الفحص:").Replace("access denied; administrator permission may be required","الوصول مرفوض؛ قد تلزم صلاحيات المسؤول")
        .Replace("locked by a process","الملف مقفل بواسطة عملية");

    private void UpdateActions()
    {
        SelectionChanged(this,null!);
        List.IsEnabled=!busy&&!batchActive;RefreshButton.IsEnabled=!busy&&!batchActive&&!refreshingInventory;
        Search.IsEnabled=SortSelector.IsEnabled=!busy&&!batchActive;
        if(kindSelector!=null)kindSelector.IsEnabled=!busy&&!batchActive;
    }

    private void SteamDbClick(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not ProgramCard card) return;
        string? url = SteamMetadataService.SteamDbUrl(card.Program);
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException) { }
    }

    private async void GamesClick(object sender, RoutedEventArgs e)
        => await UninstallSelectionAsync(false);

    // --- Automatic uninstall completion monitoring -------------------------
    // The Verify / Rescan button has been removed. Completion is detected in
    // the background by launcher-specific manifests, registry records and
    // installation folders. Opening a launcher never counts as success.
    private void StartMonitoring(ProgramCard card, UninstallSnapshot? snapshot, UninstallHistoryCapture? historyCapture, bool ownsOperation=false)
    {
        if(!ownsOperation&&!Operation.Wait(0)){SetStatus("Another removal is running; monitoring was not started.","توجد إزالة أخرى جارية؛ لم تبدأ المراقبة.");return;}
        var cts = new CancellationTokenSource();
        monitorCancellation = cts;
        busy=true;UpdateActions();
        SetRemovalControl(true,monitoring:true);
        monitorTask = MonitorRemovalAsync(card, snapshot, historyCapture, cts);
    }

    private void StopMonitoring()
    {
        var cts = monitorCancellation;
        cts?.Cancel();
        SetRemovalControl(false);
    }

    private void CancelMonitoring()
    {
        if (monitorCancellation == null) return;
        monitorCancellation.Cancel();
        SetRemovalControl(false);
        SetStatus("Monitoring stopped. The application remains listed; its uninstaller may still be running.","توقفت المراقبة. يبقى البرنامج في القائمة، وقد يظل مزيله قيد التشغيل.");
        activity.Write("INFO", "Removal monitoring cancelled by the user.");
    }

    private async Task MonitorRemovalAsync(ProgramCard card, UninstallSnapshot? snapshot, UninstallHistoryCapture? historyCapture, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            SetStatus("Waiting for the official uninstaller to finish…","بانتظار انتهاء مزيل التثبيت الرسمي…");
            activity.Write("INFO", "Automatic removal monitoring started: " + card.Program.Name);
            bool isSteam = IsVerifiedSteamGame(card.Program);
            var progress = new Progress<RemovalProbe>(probe =>
            {
                if (token.IsCancellationRequested || !ReferenceEquals(monitorCancellation, cts)) return;
                if (isSteam && !probe.LauncherRecordPresent && probe.InstallationFolderPresent)
                    SetStatus("Steam removed the installation record. Waiting for the game files to finish clearing…",
                        "أزال Steam سجل التثبيت. بانتظار اكتمال إزالة ملفات اللعبة…");
                else SetStatus(lang => BidiText.Localize(lang,"Checking removal progress… ", "جارٍ التحقق من تقدم الإزالة… ") + LocalizeProbe(probe,lang));
            });
            var result = await UninstallMonitorService.MonitorAsync(
                () => UninstallMonitorService.Probe(card.Program, snapshot), officialProcessExit, isSteam, progress, token);
            token.ThrowIfCancellationRequested();
            await CompleteRemovalAsync(card, result, historyCapture, snapshot, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or InvalidOperationException or Win32Exception)
        {
            SetStatus("Unable to confirm removal. The application remains listed. Refresh after its uninstaller closes.","تعذر تأكيد الإزالة. يبقى البرنامج في القائمة. حدّثها بعد إغلاق مزيله.");
            activity.Write("WARNING",Status.Text);CrashDiagnostics.Record("Removal monitoring",ex);
        }
        finally
        {
            cts.Dispose();
            if (ReferenceEquals(monitorCancellation, cts))
            {
                monitorCancellation = null;
                SetRemovalControl(false);
                busy=false;ReleaseRemovalOperation();UpdateActions();
            }
        }
    }

    private string LocalizeProbe(RemovalProbe probe, bool? languageOverride=null) => !(languageOverride ?? arabic)
        ? probe.Detail
        : probe.Detail.StartsWith("Probe error:",StringComparison.Ordinal)
            ? "تعذر قراءة بيانات التثبيت؛ لم يمكن تأكيد الإزالة."
        : probe.Detail=="The launched official uninstaller process is still running."
            ? "ما زال مزيل التثبيت الرسمي الذي بدأه Polar قيد التشغيل."
            : probe.RetainedSteamAssets
                ? "أزال Steam سجل تثبيت اللعبة، وبقيت صور أو سجلات لا تُشغّل اللعبة. احتُفظ بهذه الملفات."
        : probe.LauncherRecordPresent
            ? "ما زال سجل التثبيت المحدد موجودًا."
            : probe.InstallationFolderPresent
                ? "أُزيل سجل التثبيت، وما زالت بعض ملفات البرنامج موجودة."
                : "ما زال التحقق جاريًا.";

    /// <summary>
    /// Finalizes a monitored official removal: verified leftovers are detected,
    /// the four-state report is written to the Activity Log, and the list is
    /// updated only when the application itself is gone.
    /// </summary>
    private async Task CompleteRemovalAsync(ProgramCard card,RemovalProbe probe,UninstallHistoryCapture? historyCapture,UninstallSnapshot? original,CancellationToken token)
    {
        if(original==null)
        {
            SetStatus("Removal verification is incomplete: no original identity was captured.","التحقق غير مكتمل: لم تُحفظ هوية التثبيت الأصلية.");activity.Write("WARNING",Status.Text);return;
        }
        RemovalPlan? plan=officialPlan;
        try
        {
            SetStatus("Verifying removal…","جارٍ التحقق من الإزالة…");
            var current=await ReadRemovalInventoryAsync(token);
            if(probe.Confirmed)probe=await Task.Run(()=>UninstallMonitorService.Probe(card.Program,original),token);
            string originalRoot=InventoryMerger.NormalizePath(original.Program.Location);
            if(current.Any(p=>UninstallVerificationService.Matches(p,original.Program)&&InventoryMerger.NormalizePath(p.Location).Length>0&&
                !LeftoverService.EqualPath(p.Location,originalRoot)&&Directory.Exists(p.Location)))
                probe=probe with{Confirmed=false,RetainedSteamAssets=false,Detail="A matching installation now exists at a different live location; refresh before cleanup."};
            if(probe.RetainedSteamAssets)
            {
                var retained=UninstallReportService.FromProbe(card.Program,probe,null,probe.Detail);
                WriteReport(retained);
                operationOutcome=BatchItemOutcome.Succeeded;
                RemoveCard(card,original.Executables);
                if(historyCapture!=null)await RecordHistoryAsync(card,historyCapture);
                SetStatus(lang=>retained.Summary(lang)+" · "+LocalizeProbe(probe,lang));
                return;
            }
            plan??=await new RemovalEngine().PlanAsync(original,current,false,token);
            if(!probe.Confirmed)
            {
                if(batchActive)operationOutcome=BatchItemOutcome.Manual;
                var incomplete=UninstallReportService.FromProbe(card.Program,probe,null,probe.Detail,true);
                WriteReport(incomplete);
                // Recovery is read-only until the explicit Advanced action. A
                // managed/game root that cannot pass direct validation stays blocked.
                if(plan.Root!=null)await RetainEligibleContextAsync(card,plan);
                SetStatus(lang=>incomplete.Summary(lang)+" · "+LocalizeProbe(probe,lang));return;
            }
            SetStatus("Scanning eligible leftovers…","جارٍ فحص البقايا المؤهلة…");
            activity.Write("INFO",L("Verified main removal; starting authorized leftover cleanup: ","تأكدت إزالة البرنامج الأساسي؛ يبدأ تنظيف البقايا المصرح به: ")+card.Name);
            var progress=new Progress<string>(count=>SetStatus(lang => BidiText.Localize(lang,"Cleaning verified leftovers… ","جارٍ تنظيف البقايا المتحقق منها… ") + DisplayFormat.Directional(count,lang)));
            SetStatus("Cleaning verified leftovers…","جارٍ تنظيف البقايا المتحقق منها…");
            var pass=await new RemovalEngine().ExecuteAsync(plan,current,progress,token);
            foreach(var message in pass.Cleanup.Messages)activity.Write(message.StartsWith("Removed",StringComparison.Ordinal)?"INFO":"WARNING",LocalizeRemovalMessage(message));
            SetStatus("Final verification…","جارٍ التحقق النهائي…");
            var finalProbe=await Task.Run(()=>UninstallMonitorService.Probe(card.Program,original),token);
            var report=UninstallReportService.FromProbe(card.Program,finalProbe,pass.Cleanup,finalProbe.Detail,plan.Notes.Count>0);
            WriteReport(report);
            if(finalProbe.Confirmed&&!finalProbe.MainExecutablePresent)
            {
                operationOutcome=BatchItemOutcome.Succeeded;
                RemoveCard(card,original.Executables);if(historyCapture!=null)await RecordHistoryAsync(card,historyCapture);
            }
            // Changed/uncertain data is reported, but is not by itself authority
            // to offer a destructive retry. Locked verified targets remain eligible.
            bool recovery=pass.Remaining.Count>0||pass.Cleanup.FailedPaths.Count>0;
            SetAdvancedContext(card,plan,recovery,pass.Remaining);
            if(recovery)advancedDeepRemoval.ToolTip=L("Eligible resources remain after cleanup. Retry only the preserved verified scope: ","بقيت موارد مؤهلة بعد التنظيف. أعد المحاولة ضمن النطاق المتحقق منه المحفوظ: ")+card.Name;
            SetStatus(report.Summary);
        }
        catch(OperationCanceledException)
        {
            if(plan!=null)await RetainEligibleContextAsync(card,plan);
            SetStatus("Cleanup cancelled. Completed deletions cannot be undone; personal data is preserved.","أُلغي التنظيف. لا يمكن التراجع عن المحذوف؛ البيانات الشخصية محفوظة.");activity.Write("WARNING",Status.Text);
        }
        catch(Exception ex)when(RemovalEngine.IsItemError(ex)||ex is Win32Exception)
        {
            if(plan!=null)await RetainEligibleContextAsync(card,plan);
            SetStatus("Removal or cleanup verification is incomplete. Review the Activity Log; eligible recovery remains available.","التحقق من الإزالة أو التنظيف غير مكتمل. راجع سجل النشاط؛ تبقى الاستعادة المؤهلة متاحة.");activity.Write("WARNING",Status.Text+" · "+ex.GetType().Name);
        }
        finally{officialPlan=null;}
    }
    private void RemoveCard(ProgramCard card,IReadOnlyList<string>? capturedExecutables=null)
    {
        if (List.ItemsSource is IEnumerable<ProgramCard> source)
        {
            var remaining = source.Where(x => !ReferenceEquals(x, card)).ToList();
            List.ItemsSource = remaining;
            view = CollectionViewSource.GetDefaultView(remaining);
            view.Filter = FilterCard;
            view.Refresh();
            CountText.Text = L($"{remaining.Count} items", $"{remaining.Count} عنصر");
        }
        // Publish the verified removal to the shared inventory immediately so
        // the Home count reflects it, then confirm with a fresh authoritative scan.
        inventory.NotifyRemoved(card.Program,capturedExecutables);
        _ = RefreshSharedInventoryAsync();
    }

    private async Task RecordHistoryAsync(ProgramCard card, UninstallHistoryCapture capture)
    {
        try
        {
            if (await history.RecordAsync(capture))
            {
                activity.Write("INFO", "Added to recent uninstalls: " + card.Name);
                // Drop the verified removal from the persisted inventory snapshot
                // so the next scan cannot report the same product again as a new
                // external removal.
                RemoveFromSnapshot(card.Program);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            activity.Write("WARNING", "Recent uninstall history could not be updated.");
        }
    }

    /// <summary>Removes an already-recorded product from the persisted snapshot.</summary>
    private static void RemoveFromSnapshot(InstalledProgram program)
    {
        var snapshot = InventorySnapshotService.Load();
        if (snapshot.Count == 0) return;
        var remaining = snapshot
            .Where(entry => !ExternalRemovalDetector.StillPresent(entry, program))
            .ToArray();
        if (remaining.Length != snapshot.Count) InventorySnapshotService.Save(null, remaining);
    }

    private async Task Uninstall(bool deep,ProgramCard? requested=null)
    {
        var card=requested??List.SelectedItem as ProgramCard;
        if(busy||monitorCancellation!=null||card==null)return;
        if(!Operation.Wait(0)){SetStatus("An uninstall operation is already running.","توجد عملية إزالة جارية بالفعل.");return;}
        using var preparation=CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        var token=preparation.Token;directCancellation=preparation;SetRemovalControl(true);
        operationOutcome=BatchItemOutcome.Failed;monitorTask=null;
        bool transferred=false,launchSucceeded=false;UninstallSnapshot? preparedSnapshot=null;busy=true;UpdateActions();
        activity.Write("INFO",L("Starting removal: ","بدء الإزالة: ")+DisplayFormat.Isolate(card.Name));
        try
        {
            SetStatus("Verifying uninstall capabilities…","جارٍ التحقق من طريقة الإزالة…");
            var current=await ReadRemovalInventoryAsync(token);
            var decision=await Task.Run(()=>SmartUninstallSelectionService.Evaluate(card.Program,current),token);
            if(decision.Method is not (UninstallMethod.Deep or UninstallMethod.Games))
            {SetStatus(decision.Explanation);activity.Write("WARNING",Status.Text);return;}
            if(decision.GamesEnabled&&!current.Any(p=>UninstallVerificationService.Matches(p,card.Program)&&LeftoverService.EqualPath(p.Location,card.Program.Location)))
            {SetStatus("Game installation evidence changed. Refresh before removing it.","تغيّرت بيانات تثبيت اللعبة. حدّث القائمة قبل إزالتها.");return;}
            if(decision.UsesOfficialUninstaller&&!current.Any(p=>UninstallVerificationService.Matches(p,card.Program)&&string.Equals(p.Uninstall,card.Program.Uninstall,StringComparison.OrdinalIgnoreCase)))
            {SetStatus("The installation registration changed. Refresh before removing it.","تغيّر سجل التثبيت. حدّث القائمة قبل الإزالة.");return;}
            if(deep!=decision.DeepEnabled){SetStatus(decision.Explanation);return;}
            var route=decision.GameRoute;
            SetStatus("Preparing removal and authorized cleanup scope…","جارٍ تجهيز الإزالة ونطاق التنظيف المصرح به…");
            if(route?.CloseLauncher==true&&LauncherRemovalCoordinator.Supports(card.Program))
            {
                using var closeCancellation=CancellationTokenSource.CreateLinkedTokenSource(token);
                directCancellation=closeCancellation;SetRemovalControl(true);
                string launcherName=LauncherRemovalCoordinator.Profile(card.Program)!.Name;
                try
                {
                    SetStatus(lang => BidiText.Localize(lang,"Closing launcher: ","جارٍ إغلاق اللانشر: ") + DisplayFormat.Directional(launcherName,lang));
                    if(!await EpicLauncherCloseService.CloseAuthorizedAsync(
                        force=>LauncherRemovalCoordinator.CloseAsync(card.Program,force,closeCancellation.Token),
                        ()=>{SetStatus("Ending verified launcher processes; continuing automatically…","جارٍ إنهاء عمليات اللانشر المتحقق منها ثم المتابعة تلقائيًا…");activity.Write("INFO",L("Using the original removal authorization to close verified launcher processes: ","تُستخدم موافقة الإزالة الأصلية لإغلاق عمليات اللانشر المتحقق منها: ")+launcherName);},closeCancellation.Token))
                        throw new IOException("Launcher shutdown could not be verified. No uninstaller was started. Close the launcher from its tray icon and retry; no files were removed.");
                    closeCancellation.Token.ThrowIfCancellationRequested();
                    activity.Write("INFO",L("Launcher closed; continuing the same authorized game removal: ","أُغلق اللانشر؛ تُستكمل إزالة اللعبة المصرح بها نفسها: ")+launcherName);
                    SetStatus("Launcher closed. Preparing this game's uninstaller…","أُغلق اللانشر. جارٍ تجهيز مزيل هذه اللعبة…");
                }
                finally{directCancellation=preparation;SetRemovalControl(true);}
            }
            if(route?.DirectEpic==true)
            {
                using var epicCancellation=CancellationTokenSource.CreateLinkedTokenSource(token);
                directCancellation=epicCancellation;SetRemovalControl(true);
                try
                {
                    SetStatus("Closing Epic Games Launcher…","جارٍ إغلاق Epic Games Launcher…");
                    if(!await EpicLauncherCloseService.CloseAuthorizedAsync(
                        force=>EpicLauncherCloseService.CloseAsync(card.Program,force,epicCancellation.Token),
                        ()=>{SetStatus("Ending verified Epic processes; continuing automatically…","جارٍ إنهاء عمليات Epic المتحقق منها ثم المتابعة تلقائيًا…");activity.Write("INFO",L("Using the original removal authorization to close verified Epic processes.","تُستخدم موافقة الإزالة الأصلية لإغلاق عمليات Epic المتحقق منها."));},epicCancellation.Token))
                        throw new IOException("Epic shutdown could not be verified after checking replacement processes. No removal was started. Close Epic from its tray icon and retry; no files were removed.");
                    epicCancellation.Token.ThrowIfCancellationRequested();
                    SetStatus("Epic closed. Continuing removal automatically…","أُغلق Epic. تُستكمل الإزالة تلقائيًا…");
                    activity.Write("INFO",L("Verified Epic shutdown; continuing the same authorized removal operation.","تأكد إغلاق Epic؛ تُستكمل عملية الإزالة المصرح بها نفسها."));
                    var service=new EpicGameRemovalService();
                    var plan=await service.PrepareAsync(card.Program,current,epicCancellation.Token);
                    var progress=new Progress<string>(message=>SetStatus(lang => BidiText.Localize(lang,"Epic direct removal: ","إزالة Epic المباشرة: ") + DisplayFormat.Directional(message,lang)));
                    activity.Write("INFO",L("Epic direct removal authorized; preserving saves and files outside the installed build: ","تم تفويض إزالة Epic المباشرة مع حفظ بيانات الحفظ والملفات خارج قائمة التثبيت: ")+card.Name);
                    var epicCapture=await history.CaptureAsync(card.Program,token:epicCancellation.Token);
                    var result=await service.RemoveAsync(plan,current,progress,epicCancellation.Token);
                    foreach(var message in result.Messages)activity.Write(result.Confirmed?"INFO":"WARNING",message);
                    activity.Write("INFO",L("Epic metadata backup: ","نسخة بيانات Epic الاحتياطية: ")+result.Backup);
                    if(result.Confirmed)
                    {
                        operationOutcome=BatchItemOutcome.Succeeded;
                        RemoveCard(card);await RecordHistoryAsync(card,epicCapture);
                        SetStatus(lang => BidiText.Localize(lang,"Epic game files and local installation records verified as removed. Files deleted: ","تأكد حذف ملفات لعبة Epic وسجلات تثبيتها المحلية. الملفات المحذوفة: ")+DisplayFormat.Directional(DisplayFormat.Number(result.Removed),lang)+BidiText.Localize(lang,". Reopen Epic to confirm its Library view; save data was preserved.",". أعد فتح Epic للتحقق من مكتبته؛ حُفظت بيانات الحفظ."));
                    }
                    else SetStatus("Epic removal is incomplete. Review Activity Log; no success was assumed.","إزالة Epic غير مكتملة. راجع سجل النشاط؛ لم يُفترض النجاح.");
                }
                catch(OperationCanceledException)
                {SetStatus("Epic removal cancelled. Completed file deletions cannot be undone; refresh before retrying.","أُلغيت إزالة Epic. لا يمكن التراجع عن الملفات المحذوفة؛ حدّث القائمة قبل إعادة المحاولة.");activity.Write("WARNING",Status.Text);}
                catch(Exception error)when(LauncherMetadataDiscovery.ReadFailure(error)||error is Win32Exception)
                {SetStatus("Epic removal stopped. Close Epic and the game, check permissions, then refresh before retrying.","توقفت إزالة Epic. أغلق Epic واللعبة، وتحقق من الصلاحيات، ثم حدّث القائمة قبل إعادة المحاولة.");CrashDiagnostics.Record("Epic removal",error);activity.Write("WARNING",Status.Text);}
                finally{directCancellation=preparation;SetRemovalControl(true);}
                return;
            }
            var capture=await history.CaptureAsync(card.Program,token:token);
            var snapshot=await new SharedLeftoverScanner().CaptureAsync(card.Program,token);preparedSnapshot=snapshot;
            officialPlan=await new RemovalEngine().PlanAsync(snapshot,current,deep&&!decision.UsesOfficialUninstaller,token);
            advancedCard=null;advancedPlan=null;
            if(deep&&!decision.UsesOfficialUninstaller)
            {
                var prepared=officialPlan;officialPlan=null;transferred=true;
                await RunDirectPassAsync(card,null,prepared,current);return;
            }
            string target;IReadOnlyList<string> arguments;
            // Revalidate immediately before launch, not just when the row was selected.
            decision=await Task.Run(()=>SmartUninstallSelectionService.Evaluate(card.Program,current),token);
            if(decision.GamesEnabled&&decision.GameRoute!=null)
            {route=decision.GameRoute;target=route.Target;arguments=route.Arguments;}
            else if(decision.UsesOfficialUninstaller&&TryParseUninstaller(card.Program.Uninstall,out var exe,out var args))
            {target=exe;arguments=args;}
            else throw new IOException("The verified uninstall route is no longer available.");
            var start=new ProcessStartInfo(target){UseShellExecute=true};foreach(var argument in arguments)start.ArgumentList.Add(argument);
            var launchedUtc=DateTime.UtcNow;
            var process=Process.Start(start);
            if(process==null&&Path.IsPathFullyQualified(target))throw new IOException("The uninstaller process could not be started.");
            launchSucceeded=true;
            bool trackExit=route?.TrackProcess==true||decision.UsesOfficialUninstaller||(route?.ManualInteraction==false&&Path.IsPathFullyQualified(target)&&
                (Path.GetFileName(target).Contains("unins",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(target).Equals("msiexec.exe",StringComparison.OrdinalIgnoreCase)));
            if(trackExit&&process!=null)officialProcessExit=Task.Run(()=>UninstallProcessTracker.WaitAsync(process,launchedUtc));
            else{process?.Dispose();officialProcessExit=null;}
            SetStatus(lang=>route?.ManualInteraction==true?BidiText.Localize(lang,"Manual action is required inside the launcher; Polar is monitoring only.","يلزم إكمال الإزالة داخل اللانشر؛ يراقب Polar النتيجة فقط."):BidiText.Localize(lang,"Official removal started. Verifying the installation state…","بدأت الإزالة الرسمية. جارٍ التحقق من حالة التثبيت…"));
            activity.Write("INFO",L("Uninstall workflow: ","مسار الإزالة: ")+card.Name+" · "+decision.Method+(route?.ManualInteraction==true?" · manual launcher interaction required":""));
            StartMonitoring(card,snapshot,capture,true);transferred=true;
        }
        catch(OperationCanceledException){SetStatus("Uninstall preparation cancelled.","أُلغي تجهيز الإزالة.");}
        catch(Exception ex)when(LauncherMetadataDiscovery.ReadFailure(ex)||ex is Win32Exception)
                {
            if(ex is Win32Exception{NativeErrorCode:1223})
            {SetStatus("Windows authorization was cancelled. No automatic cleanup was performed.","أُلغي تفويض Windows. لم يُنفذ تنظيف تلقائي.");activity.Write("INFO",Status.Text);return;}
            // Only a missing/invalid executable can enter automatic direct
            // fallback. UAC cancellation, access denial and partial execution
            // are not authorization to delete the installation instead.
            bool missingOrInvalid=ex is FileNotFoundException or DirectoryNotFoundException||ex is Win32Exception{NativeErrorCode:2 or 3 or 193};
            if(deep&&!launchSucceeded&&preparedSnapshot!=null&&missingOrInvalid)
            {
                try
                {
                    var fresh=await ReadRemovalInventoryAsync(token);
                    DirectRemovalService.Validate(card.Program,fresh);
                    var fallback=await new RemovalEngine().PlanAsync(preparedSnapshot,fresh,true,token);
                    activity.Write("WARNING",L("The official executable is unavailable; using the authorized validated direct fallback.","المزيل الرسمي غير متاح؛ تُستخدم الإزالة المباشرة المتحقق منها ضمن التفويض."));
                    officialPlan=null;transferred=true;await RunDirectPassAsync(card,null,fallback,fresh,true);return;
                }
                catch(Exception fallbackError)when(LauncherMetadataDiscovery.ReadFailure(fallbackError))
                {activity.Write("WARNING",L("Safe direct fallback is unavailable.","الإزالة المباشرة الآمنة غير متاحة.")+" · "+fallbackError.GetType().Name);}
            }
            if(officialPlan?.Root!=null&&!launchSucceeded)await RetainEligibleContextAsync(card,officialPlan);
            SetStatus("The uninstall workflow could not be started; no success was assumed.","تعذر بدء الإزالة؛ لم يُفترض نجاحها.");activity.Write("WARNING",Status.Text+" · "+ex.GetType().Name);
        }
        finally
        {
            directCancellation=null;
            if(!transferred){busy=false;officialPlan=null;SetRemovalControl(false);ReleaseRemovalOperation();UpdateActions();}
            activity.Write(transferred||operationOutcome==BatchItemOutcome.Succeeded?"INFO":"WARNING",L("Removal dispatch finished: ","انتهى بدء الإزالة: ")+DisplayFormat.Isolate(card.Name));
        }
    }
    private void ReleaseRemovalOperation()
    {
        var pending=officialProcessExit;officialProcessExit=null;
        // Cancelling UI monitoring or navigating away must not permit a second
        // removal while our registered uninstaller is still changing files.
        if(pending is {IsCompleted:false})
            _=pending.ContinueWith(_=>Operation.Release(),CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        else Operation.Release();
    }
    private static async Task WaitForOfficialExitAsync(Process process)
    {
        using(process)await process.WaitForExitAsync();
    }
    private static bool TryParseUninstaller(string raw,out string executable,out string[] arguments) => UninstallCommandService.TryParse(raw,out executable,out arguments);
    private async void CardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border { DataContext: ProgramCard card } && !card.IconRequested)
        {
            card.IconRequested = true;
            try
            {
                var icon=await ApplicationIconResolver.ResolveAsync(card.Program);
                if(icon!=null)card.SetIcon(icon);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException
                or InvalidOperationException or FormatException or COMException)
            { CrashDiagnostics.Record("Application icon",ex); }
        }
    }
}

public sealed class ProgramCard : INotifyPropertyChanged
{
    private bool arabic;
    private bool calculating;
    private readonly string unknown;
    public InstalledProgram Program { get; }
    public string Name => string.IsNullOrWhiteSpace(Program.Name)?unknown:Program.Name.Trim();
    public long? MeasuredSizeKb {get;private set;}
    public string FallbackGlyph => string.IsNullOrWhiteSpace(Program.Identifier)?"▣":"♧";
    private string details = "";
    public string Details { get => details; private set { if (details == value) return; details = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Details))); } }
    public string Location { get; }
    public string Version { get; }
    public string Publisher { get; }
    private string size = "";
    public string Size { get => size; private set { if (size == value) return; size = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Size))); } }
    public bool IconRequested { get; set; }
    public ImageSource? Icon { get; private set; }
    public Brush Border { get; private set; } = (Brush)Application.Current.Resources["HomeBorderBrush"];
    public event PropertyChangedEventHandler? PropertyChanged;
    public ProgramCard(InstalledProgram program, bool arabic)
    {
        this.arabic = arabic;
        Program = program; unknown = "—";
        long? reportedSize = program.SizeKb is long s && s > 0 && s<=long.MaxValue/1024 ? s : null;
        MeasuredSizeKb=reportedSize;
        Version = string.IsNullOrWhiteSpace(program.Version) ? unknown : program.Version;
        Publisher = string.IsNullOrWhiteSpace(program.Publisher) ? unknown : program.Publisher;
        Size = reportedSize is long rs ? DisplayFormat.Size(rs * 1024L, arabic) : unknown;
        Details = $"{(string.IsNullOrWhiteSpace(program.Publisher) ? unknown : program.Publisher)}  ·  {(string.IsNullOrWhiteSpace(program.Version) ? unknown : program.Version)}  ·  {Size}  ·  {program.Source}";
        Location = string.IsNullOrWhiteSpace(program.Location) ? unknown : program.Location;
    }
    public void UpdateSize(long? sizeKb)
    {
        long? reported = sizeKb is long s && s > 0 && s<=long.MaxValue/1024 ? s : null;
        calculating=false;
        MeasuredSizeKb=reported;
        Size = reported is long rs ? DisplayFormat.Size(rs * 1024L, arabic) : unknown;
        Details = $"{(string.IsNullOrWhiteSpace(Program.Publisher) ? unknown : Program.Publisher)}  ·  {(string.IsNullOrWhiteSpace(Program.Version) ? unknown : Program.Version)}  ·  {Size}  ·  {Program.Source}";
    }
    public void SetCalculating()
    {
        calculating=true;
        Size = arabic ? "جارٍ الحساب…" : "Calculating…";
    }
    public void SetLanguage(bool value)
    {
        arabic=value;
        if(calculating) SetCalculating();
        else UpdateSize(MeasuredSizeKb);
    }
    public void SetIcon(ProgramIcon icon)
    {
        Icon = icon.Image;
        if (icon.Accent is Color color)
        {
            var theme = ((SolidColorBrush)Application.Current.Resources["HomeBorderBrush"]).Color;
            Border = new SolidColorBrush(Color.FromRgb((byte)((theme.R * 3 + color.R) / 4), (byte)((theme.G * 3 + color.G) / 4), (byte)((theme.B * 3 + color.B) / 4)));
        }
        PropertyChanged?.Invoke(this, new(nameof(Icon))); PropertyChanged?.Invoke(this, new(nameof(Border)));
    }
}

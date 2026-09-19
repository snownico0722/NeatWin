using System.ComponentModel;
using System.Diagnostics;
using NeatWin.Reference;
using System.Runtime.InteropServices;
using NeatWin.Core;
using NeatWin.Windows;

namespace NeatWin.App;

internal sealed class NeatWinApplicationContext : ApplicationContext
{
    private readonly WindowManager _windowManager = new();
    private readonly VisibilityAnalyzer _visibilityAnalyzer = new();
    private readonly TidyEngine _tidyEngine = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly BrowserVideoBlackBarDetector _videoBlackBarDetector = new();
    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly MainWindow _mainWindow;
    private readonly ReversibleVerticalFillManager _verticalFillManager;
    private readonly AutoTidyManager _autoTidyManager;
    private TidyOptions _tidyOptions;
    private SmartBehaviorOptions _smartBehaviorOptions;
    private readonly IntentReferenceStore _referenceStore = new();
    private bool _undoWasSmart;
    private IReadOnlyList<TidyMove> _undoPlan = [];
    private IReadOnlyList<WindowLayerOrder> _undoLayers = [];
    private readonly LayoutRunJournal _journal;
    private readonly TaskLayoutSession _taskSession = new();
    private TaskPreferences _taskPreferences = TaskPreferences.Load();
    private AutomaticLayoutMode _automaticMode;
    private readonly WorkspaceAutoTidyMonitor _workspaceMonitor;
    private bool _tidyRunning;

    public NeatWinApplicationContext()
    {
        var requestedHotkey = _settingsStore.LoadHotkey();
        _tidyOptions = _settingsStore.LoadTidyOptions();
        _smartBehaviorOptions = _settingsStore.LoadSmartBehaviorOptions();
        _verticalFillManager = new ReversibleVerticalFillManager();
        _autoTidyManager = new AutoTidyManager();
        _journal = new LayoutRunJournal(_windowManager, _referenceStore);
        _workspaceMonitor = new WorkspaceAutoTidyMonitor(_windowManager.Capture,
            () => _tidyRunning || _autoTidyManager.IsGestureActive || NativeWindowActivity.IsMoving);
        _autoTidyManager.GestureStarted += handle =>
        {
            _journal.MarkGesture(handle);
            _workspaceMonitor.GestureStarted(handle);
        };
        _autoTidyManager.GestureObserved += gesture =>
        {
            _journal.MarkGesture(gesture.WindowHandle);
            _taskSession.Gesture(gesture, DateTimeOffset.UtcNow);
            try
            {
                if (_windowManager.Capture().Any(w => w.Handle == gesture.WindowHandle && w.IsManageable && !w.IsTopmost))
                    _workspaceMonitor.GestureCompleted(gesture);
            }
            catch (Exception ex) { Trace.TraceWarning($"Gesture snapshot failed: {ex.Message}"); }
        };
        _automaticMode = _settingsStore.LoadAutomaticLayoutMode();
        if (!ConfigureAutomation(_automaticMode)) _automaticMode = AutomaticLayoutMode.Off;
        _workspaceMonitor.Requested += OnAutomaticLayoutRequested;
        _autoTidyManager.TidyRequested += OnFollowHandRequested;
        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += RunTidy;
        _mainWindow = new MainWindow(requestedHotkey, _tidyOptions, _smartBehaviorOptions, _automaticMode);
        _mainWindow.TidyRequested += (_, _) => RunTidy();
        _mainWindow.UndoRequested += (_, _) => UndoTidy();
        _mainWindow.RecorderRequested += (_, _) => OpenRecorder();
        _mainWindow.HotkeyChangeRequested += OnHotkeyChangeRequested;
        _mainWindow.TidyOptionsChangeRequested += OnTidyOptionsChangeRequested;
        _mainWindow.AutoTidyChangeRequested += OnAutoTidyChangeRequested;
        _mainWindow.ExitRequested += (_, _) => ExitThread();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open NeatWin", null, (_, _) => _mainWindow.BringToFrontFromTray());
        menu.Items.Add("Tidy visible windows", null, (_, _) => RunTidy());
        menu.Items.Add("撤销上次整理", null, (_, _) => UndoTidy());
        menu.Items.Add("打开习惯记录器", null, (_, _) => OpenRecorder());
        menu.Items.Add("人因排布偏好…", null, (_, _) => EditTaskPreferences());
        var useReference = new ToolStripMenuItem("使用记录器的弱参考") { Checked = _referenceStore.Enabled, CheckOnClick = true };
        useReference.CheckedChanged += (_, _) =>
        {
            try { _referenceStore.SetEnabled(useReference.Checked); _taskSession.Invalidate(); }
            catch (Exception ex) { _mainWindow.SetActivity($"参考开关保存失败：{ex.Message}", error: true); }
        };
        menu.Items.Add(useReference);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, Text = "NeatWin", ContextMenuStrip = menu, Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => _mainWindow.BringToFrontFromTray();
        if (_hotkeyWindow.TrySetHotkey(requestedHotkey, out var hotkeyMessage))
        {
            _mainWindow.SetHotkeyRegistration(requestedHotkey, success: true, hotkeyMessage);
            UpdateTrayText(requestedHotkey);
        }
        else _mainWindow.SetHotkeyRegistration(requestedHotkey, success: false, hotkeyMessage);
        _mainWindow.Text = "NeatWin · 人因任务排布 v3";
        _mainWindow.HandleCreated += (_, _) => AutomationGuard.MarkUtility(_mainWindow.Handle);
        AutomationGuard.MarkUtility(_mainWindow.Handle);
        _journal.Completed += OnLayoutVerified;
        _workspaceMonitor.Failed += ex => _mainWindow.SetActivity($"自动整理观察失败：{ex.Message}", error: true);
        _mainWindow.Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hotkeyWindow.HotkeyPressed -= RunTidy;
            _autoTidyManager.TidyRequested -= OnFollowHandRequested;
            _hotkeyWindow.Dispose(); _workspaceMonitor.Dispose(); _autoTidyManager.Dispose(); _journal.Dispose();
            _verticalFillManager.Dispose(); _mainWindow.Dispose();
            _trayIcon.Visible = false; _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void ExitThreadCore()
    {
        _mainWindow.AllowCloseAndClose();
        base.ExitThreadCore();
    }

    private void OnHotkeyChangeRequested(object? sender, HotkeyChangeEventArgs eventArgs)
    {
        var requested = eventArgs.Binding;
        if (_hotkeyWindow.TrySetHotkey(requested, out var message))
        {
            try { _settingsStore.SaveHotkey(requested); }
            catch (Exception exception)
            {
                _mainWindow.SetHotkeyRegistration(requested, success: true, $"快捷键已启用，但保存设置失败：{exception.Message}");
                UpdateTrayText(requested); return;
            }
            _mainWindow.SetHotkeyRegistration(requested, success: true, message);
            _mainWindow.SetActivity($"快捷键已改为 {requested}。");
            UpdateTrayText(requested); return;
        }
        var active = _hotkeyWindow.ActiveBinding ?? requested;
        _mainWindow.SetHotkeyRegistration(active, success: false, message);
    }

    private void OnTidyOptionsChangeRequested(object? sender, TidyOptionsChangeEventArgs eventArgs)
    {
        _taskSession.Invalidate();
        _tidyOptions = eventArgs.Options; _smartBehaviorOptions = eventArgs.BehaviorOptions;
        if (!_smartBehaviorOptions.PreferReversibleVerticalFill) _verticalFillManager.Track([], false);
        try
        {
            _settingsStore.SaveTidyOptions(_tidyOptions);
            _settingsStore.SaveSmartBehaviorOptions(_smartBehaviorOptions);
            _mainWindow.SetTidyOptionsStatus(_tidyOptions, _smartBehaviorOptions, success: true, string.Empty);
        }
        catch (Exception exception)
        {
            _mainWindow.SetTidyOptionsStatus(_tidyOptions, _smartBehaviorOptions, success: false,
                $"设置已在本次运行中生效，但保存失败：{exception.Message}");
        }
    }

    private bool ConfigureAutomation(AutomaticLayoutMode mode, bool arrangeNow = false)
    {
        if (mode != AutomaticLayoutMode.Off && !_autoTidyManager.IsAvailable) return false;
        if (!_workspaceMonitor.SetMode(mode, arrangeNow))
        {
            _autoTidyManager.Enabled = false;
            return false;
        }
        // Only the legacy light mode needs pointer sampling; full Smart does not.
        _autoTidyManager.Enabled = mode == AutomaticLayoutMode.LightAssist;
        return true;
    }

    private void OnAutoTidyChangeRequested(object? sender, AutoTidyChangeEventArgs eventArgs)
    {
        try
        {
            var mode = AutomaticLayoutPolicy.Read((int)eventArgs.Mode, false);
            if (!ConfigureAutomation(mode, arrangeNow: true))
            {
                ConfigureAutomation(AutomaticLayoutMode.Off);
                _automaticMode = AutomaticLayoutMode.Off;
                _mainWindow.SetAutomaticLayoutMode(_automaticMode);
                _mainWindow.SetActivity("自动整理监听不可用，已关闭自动模式；手动整理仍可用。", error: true);
                return;
            }
            _automaticMode = mode;
            _taskSession.Invalidate();
            _settingsStore.SaveAutomaticLayoutMode(mode);
            _mainWindow.SetActivity(AutomaticLayoutPolicy.Description(mode));
        }
        catch (Exception exception) { _mainWindow.SetActivity($"自动档位保存失败：{exception.Message}", error: true); }
    }

    private void OnAutomaticLayoutRequested(AutomaticLayoutRequest request)
    {
        if (request.Mode != _automaticMode) return;
        var route = AutomaticLayoutPolicy.Route(request.Mode, request.Trigger);
        if (route is not (LayoutRoute.Smart or LayoutRoute.FullTiling)) return;
        var trigger = route == LayoutRoute.FullTiling ? "automatic-tiling" :
            request.Trigger == LayoutTrigger.AfterGesture ? "automatic-gesture" : "automatic-workspace";
        RunTidy(showActivity: false, route.Value, trigger);
    }

    private void OnFollowHandRequested(ManualWindowGesture gesture)
    {
        if (_automaticMode != AutomaticLayoutMode.LightAssist || _tidyRunning) return;
        _tidyRunning = true;
        try
        {
            var snapshot = _windowManager.Capture();
            var visible = _visibilityAnalyzer.SelectVisibleWorkingSet(snapshot);
            var moved = visible.FirstOrDefault(item => item.Window.Handle == gesture.WindowHandle);
            if (moved is null || moved.Window.WorkArea != gesture.WorkArea || !SameRect(moved.Window.VisualRect, gesture.EndRect)) return;
            var interaction = _autoTidyManager.CaptureInteractionContext(visible);
            var hint = IntentEvidence.Resolve(_referenceStore.Read(), moved.Window.WorkArea, moved.Window.Dpi, DateTimeOffset.UtcNow);
            var personal = SmartPersonalizationState.Default with { PreferredGapPixels = hint.GapPixels };
            var decision = FollowHandAssistant.Decide(gesture,
                visible.Where(v => v.Window.MonitorHandle == moved.Window.MonitorHandle).ToArray(), _tidyOptions, interaction, personal);
            if (!decision.ShouldApply || decision.TargetRect == moved.Window.VisualRect ||
                (!moved.Window.IsResizable && (decision.TargetRect.Width != moved.Window.VisualRect.Width ||
                    decision.TargetRect.Height != moved.Window.VisualRect.Height))) return;
            _autoTidyManager.SuppressFor(500);
            var assisted = new[] { new TidyMove(moved.Window, decision.TargetRect) };
            _workspaceMonitor.ApplicationRequested(snapshot, new(assisted, [], []));
            _windowManager.Apply(assisted);
            _journal.Record(snapshot, new(assisted, [], []), "follow-hand", []);
        }
        catch { /* Assistance must not interrupt the completed manual action. */ }
        finally { _tidyRunning = false; }
    }

    private void RunTidy() => RunTidy(showActivity: true,
        AutomaticLayoutPolicy.Route(_automaticMode, LayoutTrigger.Manual) ?? LayoutRoute.ManualAlgorithm);

    private void RunTidy(bool showActivity, LayoutRoute route = LayoutRoute.ManualAlgorithm, string? source = null)
    {
        if (_tidyRunning || (_autoTidyManager.IsGestureActive || NativeWindowActivity.IsMoving)) return;
        _tidyRunning = true;
        try
        {
            var tiling = route == LayoutRoute.FullTiling;
            var effectiveOptions = route == LayoutRoute.Smart ? _tidyOptions with { AlgorithmMode = TidyAlgorithmMode.Smart } : _tidyOptions;
            var smart = !tiling && effectiveOptions.AlgorithmMode == TidyAlgorithmMode.Smart;
            var trigger = source ?? (showActivity ? tiling ? "explicit-tiling" : smart ? "explicit" : "explicit-classic" : "classic-follow");
            var snapshot = _windowManager.Capture();
            var visibleWorkingSet = _visibilityAnalyzer.SelectVisibleWorkingSet(snapshot,
                includeStackAccess: smart);
            VideoBlackBarHint? videoHint = null;
            if (smart && _smartBehaviorOptions.RemoveVideoBlackBars)
                videoHint = _videoBlackBarDetector.TryDetect(visibleWorkingSet);
            IReadOnlyList<TidyMove> plan;
            IntentLayoutPlan? detailed = null;
            if (tiling)
            {
                detailed = LayoutDispatch.Create(route, visibleWorkingSet, effectiveOptions, null, snapshot, null);
                plan = detailed.Moves;
            }
            else if (smart)
            {
                var taskContext = _taskSession.Capture(snapshot, _taskPreferences.Profile, Displays(snapshot),
                    _taskPreferences.Calibration, DateTimeOffset.UtcNow) with
                { PreferReversibleVerticalFill = _smartBehaviorOptions.PreferReversibleVerticalFill };
                if (videoHint is { Confidence: >= .82 })
                    taskContext = taskContext with { WindowHints = [new(videoHint.WindowHandle, PassiveVisual: true)], VideoHint = videoHint };
                detailed = LayoutDispatch.Create(route, visibleWorkingSet, effectiveOptions, _referenceStore.Read(), snapshot, taskContext);
                plan = detailed.Moves;
                // The task objective replaces the old geometry-only fill pass. A video refinement
                // is retained only when task utility and screen-aware access survive.
                var beforeVideoPlan = plan;
                plan = VideoAspectPostProcessor.Refine(visibleWorkingSet, plan, effectiveOptions, _smartBehaviorOptions, videoHint);
                if (!IntentLayoutPlanner.TaskRefinementAcceptable(visibleWorkingSet, detailed, plan, effectiveOptions, taskContext, snapshot))
                    plan = detailed.Moves;
                if (videoHint is not null)
                {
                    var browser = visibleWorkingSet.FirstOrDefault(item => item.Window.Handle == videoHint.WindowHandle);
                    if (browser is not null)
                    {
                        var before = beforeVideoPlan.FirstOrDefault(move => move.Window.Handle == videoHint.WindowHandle)?.TargetVisualRect ?? browser.Window.VisualRect;
                        var after = plan.FirstOrDefault(move => move.Window.Handle == videoHint.WindowHandle)?.TargetVisualRect ?? browser.Window.VisualRect;
                        if (before != after) _videoBlackBarDetector.RecordApplied(videoHint.WindowHandle, after);
                    }
                }
            }
            else plan = _tidyEngine.CreatePlan(visibleWorkingSet, effectiveOptions);

            detailed = detailed is null ? new(plan, [], []) : detailed with { Moves = plan };
            _autoTidyManager.SuppressFor(650);
            _workspaceMonitor.ApplicationRequested(snapshot, detailed);
            var application = _windowManager.ApplyLayout(detailed);
            detailed = application.Plan; plan = detailed.Moves;
            _verticalFillManager.Track(plan, smart && _smartBehaviorOptions.PreferReversibleVerticalFill);
            var layerOrders = detailed.Layers;
            if (plan.Count > 0 || layerOrders.Count > 0) { _undoPlan = plan; _undoLayers = layerOrders; _undoWasSmart = smart; }
            if (smart)
                _taskSession.Requested(snapshot, detailed, DateTimeOffset.UtcNow);
            _journal.Record(snapshot, detailed, trigger, application.Notes);
            if (showActivity)
            {
                var kinds = string.Join("、", detailed.Groups.Select(g => KindName(g.Selected)).Distinct());
                var message = plan.Count == 0 && layerOrders.Count == 0
                    ? $"已检查 {visibleWorkingSet.Count} 个当前可见窗口，本次保留原状，未找到更合适的可执行方案。"
                    : $"{kinds}：已请求调整 {plan.Count} 个窗口、{layerOrders.Count} 组层级。";
                _mainWindow.SetActivity(application.Notes.Any(n => n.Contains("未")) ? message + " " + string.Join(" ", application.Notes) : message);
            }
        }
        catch (Exception exception)
        {
            _mainWindow.SetActivity($"整理失败：{exception.Message}", error: true);
            if (showActivity) _trayIcon.ShowBalloonTip(3000, "NeatWin", exception.Message, ToolTipIcon.Error);
        }
        finally { _tidyRunning = false; }
    }

    private static bool SameRect(RectI a, RectI b) =>
        Math.Abs(a.X - b.X) <= 3 && Math.Abs(a.Y - b.Y) <= 3 &&
        Math.Abs(a.Width - b.Width) <= 3 && Math.Abs(a.Height - b.Height) <= 3;

    private void UndoTidy()
    {
        if (_tidyRunning || (_autoTidyManager.IsGestureActive || NativeWindowActivity.IsMoving) || (_undoPlan.Count == 0 && _undoLayers.Count == 0))
        { _mainWindow.SetActivity("没有可撤销的整理。"); return; }
        try
        {
            var current = _windowManager.Capture().ToDictionary(w => w.Handle);
            var reverse = LayoutUndoPlanner.Create(new(_undoPlan, _undoLayers, []), current.Values.ToArray());
            _autoTidyManager.SuppressFor(650);
            _workspaceMonitor.ApplicationRequested(current.Values.ToArray(), reverse);
            var application = _windowManager.ApplyLayout(reverse);
            if (_undoWasSmart)
                _taskSession.RejectExplicitly(application.Plan.Moves.Select(m => m.Window.Handle)
                    .Concat(application.Plan.Layers.SelectMany(l => l.FrontToBack.Select(w => w.Handle))));
            _journal.Record(current.Values.ToArray(), application.Plan, "undo", application.Notes);
            var skipped = _undoPlan.Count - application.Plan.Moves.Count;
            _undoPlan = []; _undoLayers = []; _undoWasSmart = false;
            _mainWindow.SetActivity($"已请求还原 {application.Plan.Moves.Count} 个窗口；跳过 {skipped} 个已再次调整、关闭或状态改变的窗口。");
        }
        catch (Exception ex) { _mainWindow.SetActivity($"撤销失败：{ex.Message}", error: true); }
    }

    private static string KindName(string kind) => kind switch
    {
        "full-tiling" => "完整平铺", "tiling-insufficient-space" => "可平铺空间不足，保留原状",
        "task-vertical-fill" => "可逆纵向填满",
        "task-edge" => "共同观看贴边", "task-fit" => "内容尺度调整", "task-bleed" => "边缘空间交换",
        "task-columns" => "保留列关系重排", "rescue" => "恢复可操作区域",
        "edge-row" => "叠边并排", "stack" => "成组叠放", "restack" => "调整前后层级",
        "columns" => "左右排布", "rows" => "上下排布", "grid" => "多行排布", "local" => "局部微调", _ => "保留当前关系",
    };

    private void OnLayoutVerified(LayoutRunObservation observation)
    {
        if (observation.Trigger is not ("explicit" or "automatic-gesture" or "automatic-workspace" or "explicit-tiling" or "automatic-tiling") ||
            observation.Actual is null || observation.Outcome == "intervened") return;
        if (observation.Trigger is "explicit" or "automatic-gesture" or "automatic-workspace")
        {
            if (observation.Outcome == "observed-match") _taskSession.Verify(_windowManager.Capture());
            else _taskSession.Invalidate();
        }
        var actual = observation.Actual.Windows;
        var matched = observation.Targets.Count(t => actual.Any(w => w.Id == t.Id && SameRect(w.Rect, t.Target)));
        var description = string.Join("、", observation.Groups.Select(g => KindName(g.Selected)).Distinct());
        _mainWindow.SetActivity(observation.Outcome == "observed-match"
            ? $"{description}：已确认 {matched} 个窗口的目标位置；层级已核对。"
            : $"{description}：{matched}/{observation.Targets.Length} 个位置匹配；其余可能被应用限制，诊断已记录。");
        if (observation.ApplyNotes.Any(n => n.Contains("未执行") || n.Contains("未套用") || n.Contains("未覆盖")))
            _mainWindow.SetActivity(string.Join(" ", observation.ApplyNotes));
    }

    private static TaskDisplay[] Displays(IReadOnlyList<WindowSnapshot> snapshot) =>
        Screen.AllScreens.Select((screen, index) =>
        {
            var work = screen.WorkingArea; var bounds = screen.Bounds;
            var area = new RectI(work.X, work.Y, work.Width, work.Height);
            var monitor = snapshot.FirstOrDefault(w => w.WorkArea == area)?.MonitorHandle ?? (nint)(-index - 1);
            return new TaskDisplay(monitor, area, new(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }).ToArray();

    private void EditTaskPreferences()
    {
        var work = Screen.FromControl(_mainWindow).WorkingArea;
        using var dialog = new TaskPreferencesDialog(_taskPreferences,
            new(work.X, work.Y, work.Width, work.Height), (uint)_mainWindow.DeviceDpi);
        if (dialog.ShowDialog(_mainWindow) != DialogResult.OK) return;
        try
        {
            var value = dialog.Value; value.Save(); _taskPreferences = value;
            _taskSession.Invalidate();
            _mainWindow.SetActivity("人因偏好已保存；下一次主动整理使用新偏好。");
        }
        catch (Exception ex) { _mainWindow.SetActivity($"保存失败：{ex.Message}", error: true); }
    }

    private void OpenRecorder()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "NeatWin.Recorder.exe");
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException("请将 NeatWin.Recorder.exe 放在 NeatWin.exe 同一目录。", path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { _mainWindow.SetActivity($"无法打开记录器：{ex.Message}", error: true); }
    }

    private void UpdateTrayText(HotkeyBinding binding)
    {
        var text = $"NeatWin — {binding}";
        _trayIcon.Text = text.Length <= 63 ? text : "NeatWin";
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 1;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private bool _disposed;
    private bool _registered;
    internal HotkeyWindow()
    {
        CreateHandle(new CreateParams { Caption = "NeatWin.HotkeyWindow", Parent = NativeMethods.HwndMessage });
    }
    internal event Action? HotkeyPressed;
    internal HotkeyBinding? ActiveBinding { get; private set; }

    internal bool TrySetHotkey(HotkeyBinding binding, out string message)
    {
        if (_disposed) { message = "快捷键宿主已关闭。"; return false; }
        var previous = ActiveBinding;
        if (_registered) { _ = NativeMethods.UnregisterHotKey(Handle, HotkeyId); _registered = false; }
        if (NativeMethods.RegisterHotKey(Handle, HotkeyId, binding.NativeModifiers, (uint)binding.Key))
        {
            ActiveBinding = binding; _registered = true; message = $"快捷键已启用：{binding}"; return true;
        }
        var error = Marshal.GetLastWin32Error();
        if (previous is HotkeyBinding previousBinding && NativeMethods.RegisterHotKey(Handle, HotkeyId,
            previousBinding.NativeModifiers, (uint)previousBinding.Key))
        { ActiveBinding = previousBinding; _registered = true; }
        else ActiveBinding = null;
        message = error == ErrorHotkeyAlreadyRegistered
            ? $"{binding} 已被其他程序占用，请换一组。"
            : new Win32Exception(error, $"无法注册快捷键 {binding}。").Message;
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registered) _ = NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        DestroyHandle(); GC.SuppressFinalize(this);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotkey && message.WParam == HotkeyId)
        { HotkeyPressed?.Invoke(); return; }
        base.WndProc(ref message);
    }
}

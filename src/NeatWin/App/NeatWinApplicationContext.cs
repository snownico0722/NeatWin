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
    private IReadOnlyList<TidyMove> _undoPlan = [];
    private IReadOnlyList<WindowLayerOrder> _undoLayers = [];
    private readonly LayoutRunJournal _journal;
    private bool _autoTidyEnabled;
    private bool _tidyRunning;

    public NeatWinApplicationContext()
    {
        var requestedHotkey = _settingsStore.LoadHotkey();
        _tidyOptions = _settingsStore.LoadTidyOptions();
        _smartBehaviorOptions = _settingsStore.LoadSmartBehaviorOptions();
        _verticalFillManager = new ReversibleVerticalFillManager();
        _autoTidyManager = new AutoTidyManager();
        _journal = new LayoutRunJournal(_windowManager, _referenceStore);
        _autoTidyManager.GestureObserved += gesture => _journal.MarkGesture(gesture.WindowHandle);
        _autoTidyEnabled = _settingsStore.LoadAutoTidyEnabled() && _autoTidyManager.IsAvailable;
        _autoTidyManager.Enabled = _autoTidyEnabled;
        _autoTidyManager.TidyRequested += OnFollowHandRequested;

        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += RunTidy;

        _mainWindow = new MainWindow(
            requestedHotkey,
            _tidyOptions,
            _smartBehaviorOptions,
            _autoTidyEnabled);
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
        var useReference = new ToolStripMenuItem("使用记录器的弱参考") { Checked = _referenceStore.Enabled, CheckOnClick = true };
        useReference.CheckedChanged += (_, _) =>
        {
            try { _referenceStore.SetEnabled(useReference.Checked); }
            catch (Exception ex) { _mainWindow.SetActivity($"参考开关保存失败：{ex.Message}", error: true); }
        };
        menu.Items.Add(useReference);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "NeatWin",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => _mainWindow.BringToFrontFromTray();

        if (_hotkeyWindow.TrySetHotkey(requestedHotkey, out var hotkeyMessage))
        {
            _mainWindow.SetHotkeyRegistration(requestedHotkey, success: true, hotkeyMessage);
            UpdateTrayText(requestedHotkey);
        }
        else
        {
            _mainWindow.SetHotkeyRegistration(requestedHotkey, success: false, hotkeyMessage);
        }

        _mainWindow.Text = "NeatWin · 叠边与层级 v2";
        _mainWindow.HandleCreated += (_, _) => AutomationGuard.MarkUtility(_mainWindow.Handle);
        AutomationGuard.MarkUtility(_mainWindow.Handle);
        _journal.Completed += OnLayoutVerified;
        _mainWindow.Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hotkeyWindow.HotkeyPressed -= RunTidy;
            _autoTidyManager.TidyRequested -= OnFollowHandRequested;
            _hotkeyWindow.Dispose();
            _autoTidyManager.Dispose();
            _journal.Dispose();
            _verticalFillManager.Dispose();
            _mainWindow.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
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
            try
            {
                _settingsStore.SaveHotkey(requested);
            }
            catch (Exception exception)
            {
                _mainWindow.SetHotkeyRegistration(requested, success: true, $"快捷键已启用，但保存设置失败：{exception.Message}");
                UpdateTrayText(requested);
                return;
            }

            _mainWindow.SetHotkeyRegistration(requested, success: true, message);
            _mainWindow.SetActivity($"快捷键已改为 {requested}。");
            UpdateTrayText(requested);
            return;
        }

        var active = _hotkeyWindow.ActiveBinding ?? requested;
        _mainWindow.SetHotkeyRegistration(active, success: false, message);
    }

    private void OnTidyOptionsChangeRequested(object? sender, TidyOptionsChangeEventArgs eventArgs)
    {
        _tidyOptions = eventArgs.Options;
        _smartBehaviorOptions = eventArgs.BehaviorOptions;

        try
        {
            _settingsStore.SaveTidyOptions(_tidyOptions);
            _settingsStore.SaveSmartBehaviorOptions(_smartBehaviorOptions);
            _mainWindow.SetTidyOptionsStatus(
                _tidyOptions,
                _smartBehaviorOptions,
                success: true,
                string.Empty);
        }
        catch (Exception exception)
        {
            _mainWindow.SetTidyOptionsStatus(
                _tidyOptions,
                _smartBehaviorOptions,
                success: false,
                $"设置已在本次运行中生效，但保存失败：{exception.Message}");
        }
    }

    private void OnAutoTidyChangeRequested(object? sender, AutoTidyChangeEventArgs eventArgs)
    {
        if (eventArgs.Enabled && !_autoTidyManager.IsAvailable)
        {
            _autoTidyEnabled = false;
            _autoTidyManager.Enabled = false;
            _mainWindow.SetAutoTidyEnabled(false);
            _mainWindow.SetActivity("自动整理监听不可用。", error: true);
            return;
        }

        _autoTidyEnabled = eventArgs.Enabled;
        _autoTidyManager.Enabled = _autoTidyEnabled;

        try
        {
            _settingsStore.SaveAutoTidyEnabled(_autoTidyEnabled);
        }
        catch (Exception exception)
        {
            _mainWindow.SetActivity($"自动整理已在本次运行中生效，但保存失败：{exception.Message}", error: true);
        }
    }

    private void OnFollowHandRequested(ManualWindowGesture gesture)
    {
        if (!_autoTidyEnabled || _tidyRunning)
        {
            return;
        }

        if (_tidyOptions.AlgorithmMode != TidyAlgorithmMode.Smart)
        {
            RunTidy(showActivity: false);
            return;
        }

        _tidyRunning = true;
        try
        {
            var snapshot = _windowManager.Capture();
            var visible = _visibilityAnalyzer.SelectVisibleWorkingSet(snapshot);
            var moved = visible.FirstOrDefault(item => item.Window.Handle == gesture.WindowHandle);
            if (moved is null || moved.Window.WorkArea != gesture.WorkArea ||
                !SameRect(moved.Window.VisualRect, gesture.EndRect))
            {
                return;
            }

            var interaction = _autoTidyManager.CaptureInteractionContext(visible);
            var hint = IntentEvidence.Resolve(_referenceStore.Read(), moved.Window.WorkArea,
                moved.Window.Dpi, DateTimeOffset.UtcNow);
            // Legacy learned weights stay on disk for compatibility but no longer steer the product.
            var personal = SmartPersonalizationState.Default with { PreferredGapPixels = hint.GapPixels };
            var decision = FollowHandAssistant.Decide(
                gesture,
                visible.Where(v => v.Window.MonitorHandle == moved.Window.MonitorHandle).ToArray(),
                _tidyOptions,
                interaction,
                personal);
            if (!decision.ShouldApply || decision.TargetRect == moved.Window.VisualRect ||
                (!moved.Window.IsResizable && (decision.TargetRect.Width != moved.Window.VisualRect.Width ||
                    decision.TargetRect.Height != moved.Window.VisualRect.Height)))
            {
                return;
            }

            _autoTidyManager.SuppressFor(500);
            var assisted = new[] { new TidyMove(moved.Window, decision.TargetRect) };
            _windowManager.Apply(assisted);
            _journal.Record(snapshot, new(assisted, [], []), "follow-hand", []);
        }
        catch
        {
            // Follow-hand assistance is deliberately quiet and best-effort. The user's raw move is
            // already complete, so failure here should never interrupt their workflow.
        }
        finally
        {
            _tidyRunning = false;
        }
    }

    private void RunTidy() => RunTidy(showActivity: true);

    private void RunTidy(bool showActivity)
    {
        if (_tidyRunning || (_autoTidyManager.IsGestureActive || NativeWindowActivity.IsMoving))
        {
            return;
        }

        _tidyRunning = true;
        try
        {
            var snapshot = _windowManager.Capture();
            var visibleWorkingSet = _visibilityAnalyzer.SelectVisibleWorkingSet(snapshot,
                includeStackAccess: _tidyOptions.AlgorithmMode == TidyAlgorithmMode.Smart);

            VideoBlackBarHint? videoHint = null;
            if (_tidyOptions.AlgorithmMode == TidyAlgorithmMode.Smart &&
                _smartBehaviorOptions.RemoveVideoBlackBars)
            {
                videoHint = _videoBlackBarDetector.TryDetect(visibleWorkingSet);
            }

            IReadOnlyList<TidyMove> plan;
            IntentLayoutPlan? detailed = null;
            if (_tidyOptions.AlgorithmMode == TidyAlgorithmMode.Smart)
            {
                detailed = IntentLayoutPlanner.CreateDetailedPlan(visibleWorkingSet, _tidyOptions, _referenceStore.Read(), snapshot);
                plan = detailed.Moves;
                var intendedPlan = plan;

                plan = HumanCenteredExplicitBehaviors.Refine(
                    visibleWorkingSet,
                    plan,
                    _tidyOptions,
                    _smartBehaviorOptions, preservePlannedOverlap: true);

                var beforeVideoPlan = plan;
                plan = VideoAspectPostProcessor.Refine(
                    visibleWorkingSet,
                    plan,
                    _tidyOptions,
                    _smartBehaviorOptions,
                    videoHint);

                if (!IntentLayoutPlanner.RefinementPreservesExposure(visibleWorkingSet, intendedPlan, plan, detailed.Layers))
                    plan = intendedPlan;

                if (videoHint is not null)
                {
                    var browser = visibleWorkingSet.FirstOrDefault(item => item.Window.Handle == videoHint.WindowHandle);
                    if (browser is not null)
                    {
                        var before = beforeVideoPlan
                            .FirstOrDefault(move => move.Window.Handle == videoHint.WindowHandle)?.TargetVisualRect ??
                            browser.Window.VisualRect;
                        var after = plan
                            .FirstOrDefault(move => move.Window.Handle == videoHint.WindowHandle)?.TargetVisualRect ??
                            browser.Window.VisualRect;
                        if (before != after)
                        {
                            _videoBlackBarDetector.RecordApplied(videoHint.WindowHandle, after);
                        }
                    }
                }
            }
            else
            {
                plan = _tidyEngine.CreatePlan(visibleWorkingSet, _tidyOptions);
            }

            detailed = detailed is null ? new(plan, [], []) : detailed with { Moves = plan };
            _autoTidyManager.SuppressFor(650);
            var application = _windowManager.ApplyLayout(detailed);
            detailed = application.Plan;
            plan = detailed.Moves;
            _verticalFillManager.Track(
                plan,
                _tidyOptions.AlgorithmMode == TidyAlgorithmMode.Smart &&
                _smartBehaviorOptions.PreferReversibleVerticalFill);

            var layerOrders = detailed.Layers;
            if (plan.Count > 0 || layerOrders.Count > 0)
            {
                _undoPlan = plan;
                _undoLayers = layerOrders;
            }
            _journal.Record(snapshot, detailed, showActivity ? "explicit" : "classic-follow", application.Notes);
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
            _trayIcon.ShowBalloonTip(
                3000,
                "NeatWin",
                exception.Message,
                ToolTipIcon.Error);
        }
        finally
        {
            _tidyRunning = false;
        }
    }

    private static bool SameRect(RectI a, RectI b) =>
        Math.Abs(a.X - b.X) <= 3 && Math.Abs(a.Y - b.Y) <= 3 &&
        Math.Abs(a.Width - b.Width) <= 3 && Math.Abs(a.Height - b.Height) <= 3;

    private void UndoTidy()
    {
        if (_tidyRunning || (_autoTidyManager.IsGestureActive || NativeWindowActivity.IsMoving) || (_undoPlan.Count == 0 && _undoLayers.Count == 0))
        {
            _mainWindow.SetActivity("没有可撤销的整理。");
            return;
        }
        try
        {
            var current = _windowManager.Capture().ToDictionary(w => w.Handle);
            var reverse = new List<TidyMove>();
            foreach (var move in _undoPlan)
                if (current.TryGetValue(move.Window.Handle, out var window) && window.IsManageable &&
                    window.ProcessId == move.Window.ProcessId &&
                    window.MonitorHandle == move.Window.MonitorHandle && window.WorkArea == move.Window.WorkArea &&
                    SameRect(window.VisualRect, move.TargetVisualRect))
                    reverse.Add(new TidyMove(window, move.Window.VisualRect));
            var reverseLayers = _undoLayers.Where(l => WindowLayerSafety.MatchesOrder(l, current.Values.ToArray()) &&
                l.FrontToBack.All(w => current.TryGetValue(w.Handle, out var now) && now.ProcessId == w.ProcessId &&
                    SameRect(now.VisualRect, _undoPlan.FirstOrDefault(m => m.Window.Handle == w.Handle)?.TargetVisualRect ?? w.VisualRect)))
                .Select(l => new WindowLayerOrder(l.FrontToBack.OrderBy(w => w.ZOrder).ToArray())).ToArray();
            var restorable = reverseLayers.SelectMany(l => l.FrontToBack.Select(w => w.Handle)).ToHashSet();
            var blocked = _undoLayers.SelectMany(l => l.FrontToBack.Select(w => w.Handle))
                .Where(h => !restorable.Contains(h)).ToHashSet();
            // Do not restore the geometry of a group whose required relative order was changed.
            reverse.RemoveAll(m => blocked.Contains(m.Window.Handle));
            _autoTidyManager.SuppressFor(650);
            var application = _windowManager.ApplyLayout(new(reverse, reverseLayers, []));
            _journal.Record(current.Values.ToArray(), application.Plan, "undo", application.Notes);
            var skipped = _undoPlan.Count - application.Plan.Moves.Count;
            _undoPlan = [];
            _undoLayers = [];
            _mainWindow.SetActivity($"已请求还原 {application.Plan.Moves.Count} 个窗口；跳过 {skipped} 个已再次调整、关闭或状态改变的窗口。");
        }
        catch (Exception ex) { _mainWindow.SetActivity($"撤销失败：{ex.Message}", error: true); }
    }

    private static string KindName(string kind) => kind switch
    {
        "edge-row" => "叠边并排", "stack" => "成组叠放", "restack" => "调整前后层级",
        "columns" => "左右排布", "rows" => "上下排布", "grid" => "多行排布", "local" => "局部微调", _ => "保留当前关系",
    };

    private void OnLayoutVerified(LayoutRunObservation observation)
    {
        if (observation.Trigger != "explicit" || observation.Actual is null || observation.Outcome == "intervened") return;
        var actual = observation.Actual.Windows;
        var matched = observation.Targets.Count(t => actual.Any(w => w.Id == t.Id && SameRect(w.Rect, t.Target)));
        var description = string.Join("、", observation.Groups.Select(g => KindName(g.Selected)).Distinct());
        _mainWindow.SetActivity(observation.Outcome == "observed-match"
            ? $"{description}：已确认 {matched} 个窗口的目标位置；层级已核对。"
            : $"{description}：{matched}/{observation.Targets.Length} 个位置匹配；其余可能被应用限制，诊断已记录。");
        if (observation.ApplyNotes.Any(n => n.Contains("未执行") || n.Contains("未套用") || n.Contains("未覆盖")))
            _mainWindow.SetActivity(string.Join(" ", observation.ApplyNotes));
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
        CreateHandle(new CreateParams
        {
            Caption = "NeatWin.HotkeyWindow",
            Parent = NativeMethods.HwndMessage,
        });
    }

    internal event Action? HotkeyPressed;

    internal HotkeyBinding? ActiveBinding { get; private set; }

    internal bool TrySetHotkey(HotkeyBinding binding, out string message)
    {
        if (_disposed)
        {
            message = "快捷键宿主已关闭。";
            return false;
        }

        var previous = ActiveBinding;
        if (_registered)
        {
            _ = NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }

        if (NativeMethods.RegisterHotKey(
                Handle,
                HotkeyId,
                binding.NativeModifiers,
                (uint)binding.Key))
        {
            ActiveBinding = binding;
            _registered = true;
            message = $"快捷键已启用：{binding}";
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        if (previous is HotkeyBinding previousBinding &&
            NativeMethods.RegisterHotKey(
                Handle,
                HotkeyId,
                previousBinding.NativeModifiers,
                (uint)previousBinding.Key))
        {
            ActiveBinding = previousBinding;
            _registered = true;
        }
        else
        {
            ActiveBinding = null;
        }

        message = error == ErrorHotkeyAlreadyRegistered
            ? $"{binding} 已被其他程序占用，请换一组。"
            : new Win32Exception(error, $"无法注册快捷键 {binding}。").Message;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_registered)
        {
            _ = NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        }

        DestroyHandle();
        GC.SuppressFinalize(this);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotkey && message.WParam == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            return;
        }

        base.WndProc(ref message);
    }
}

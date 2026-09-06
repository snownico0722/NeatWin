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
    private bool _autoTidyEnabled;
    private bool _tidyRunning;

    public NeatWinApplicationContext()
    {
        var requestedHotkey = _settingsStore.LoadHotkey();
        _tidyOptions = _settingsStore.LoadTidyOptions();
        _smartBehaviorOptions = _settingsStore.LoadSmartBehaviorOptions();
        _verticalFillManager = new ReversibleVerticalFillManager();
        _autoTidyManager = new AutoTidyManager();
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
            _windowManager.Apply([new TidyMove(moved.Window, decision.TargetRect)]);
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
        if (_tidyRunning)
        {
            return;
        }

        _tidyRunning = true;
        try
        {
            var snapshot = _windowManager.Capture();
            var visibleWorkingSet = _visibilityAnalyzer.SelectVisibleWorkingSet(snapshot);

            VideoBlackBarHint? videoHint = null;
            if (_tidyOptions.AlgorithmMode == TidyAlgorithmMode.Smart &&
                _smartBehaviorOptions.RemoveVideoBars)
            {
                videoHint = _videoBlackBarDetector.TryDetect(visibleWorkingSet);
            }

            IReadOnlyList<TidyMove> plan;
            if (_tidyOptions.AlgorithmMode == TidyAlgorithmMode.Smart)
            {
                plan = IntentLayoutPlanner.CreatePlan(visibleWorkingSet, _tidyOptions, _referenceStore.Read());

                plan = HumanCenteredExplicitBehaviors.Refine(
                    visibleWorkingSet,
                    plan,
                    _tidyOptions,
                    _smartBehaviorOptions);

                var beforeVideoPlan = plan;
                plan = VideoAspectPostProcessor.Refine(
                    visibleWorkingSet,
                    plan,
                    _tidyOptions,
                    _smartBehaviorOptions,
                    videoHint);

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

            _verticalFillManager.Track(
                plan,
                _tidyOptions.AlgorithmMode == TidyAlgorithmMode.Smart &&
                _smartBehaviorOptions.PreferReversibleVerticalFill);

            if (plan.Count > 0)
            {
                _autoTidyManager.SuppressFor(650);
                _windowManager.Apply(plan);

                _undoPlan = plan;
            }

            if (showActivity)
            {
                var message = plan.Count == 0
                    ? $"已检查 {visibleWorkingSet.Count} 个当前可见窗口，没有需要调整的地方。"
                    : $"整理完成：检查 {visibleWorkingSet.Count} 个当前可见窗口，调整 {plan.Count} 个。";
                _mainWindow.SetActivity(message);
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
        if (_tidyRunning || _undoPlan.Count == 0)
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
            _autoTidyManager.SuppressFor(650);
            _windowManager.Apply(reverse);
            var skipped = _undoPlan.Count - reverse.Count;
            _undoPlan = [];
            _mainWindow.SetActivity($"已还原 {reverse.Count} 个窗口；跳过 {skipped} 个已再次调整、关闭或状态改变的窗口。");
        }
        catch (Exception ex) { _mainWindow.SetActivity($"撤销失败：{ex.Message}", error: true); }
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
            message = "快捷键已启用：{binding}";
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

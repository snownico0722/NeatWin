using System.ComponentModel;
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
    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly MainWindow _mainWindow;
    private readonly ReversibleVerticalFillManager _verticalFillManager;
    private readonly AutoTidyManager _autoTidyManager;
    private TidyOptions _tidyOptions;
    private SmartBehaviorOptions _smartBehaviorOptions;
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
        _autoTidyManager.TidyRequested += OnAutoTidyRequested;

        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += RunTidy;

        _mainWindow = new MainWindow(
            requestedHotkey,
            _tidyOptions,
            _smartBehaviorOptions,
            _autoTidyEnabled);
        _mainWindow.TidyRequested += (_, _) => RunTidy();
        _mainWindow.HotkeyChangeRequested += OnHotkeyChangeRequested;
        _mainWindow.TidyOptionsChangeRequested += OnTidyOptionsChangeRequested;
        _mainWindow.AutoTidyChangeRequested += OnAutoTidyChangeRequested;
        _mainWindow.ExitRequested += (_, _) => ExitThread();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open NeatWin", null, (_, _) => _mainWindow.BringToFrontFromTray());
        menu.Items.Add("Tidy visible windows", null, (_, _) => RunTidy());
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
            _autoTidyManager.TidyRequested -= OnAutoTidyRequested;
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
            _mainWindow.SetActivity($"快捷键已改为 {requested}。鼠标按钮仍然随时可用。");
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
                "整理设置已保存并立即生效。");
            _mainWindow.SetActivity("整理设置已更新；下一次整理会使用新偏好。");
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

    private void OnAutoTidyRequested()
    {
        if (_autoTidyEnabled)
        {
            RunTidy(showActivity: false);
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
            IReadOnlyList<TidyMove> plan = _tidyEngine.CreatePlan(visibleWorkingSet, _tidyOptions);

            if (_tidyOptions.AlgorithmMode == TidyAlgorithmMode.Smart)
            {
                plan = SmartPlanPostProcessor.Refine(
                    visibleWorkingSet,
                    plan,
                    _tidyOptions,
                    _smartBehaviorOptions);
            }

            _verticalFillManager.Track(
                plan,
                _tidyOptions.AlgorithmMode == TidyAlgorithmMode.Smart &&
                _smartBehaviorOptions.PreferReversibleVerticalFill);

            if (plan.Count > 0)
            {
                // Some applications may report a move/size-end event after NeatWin changes their
                // rectangle. Suppress that short tail so automatic tidy cannot feed back into itself.
                _autoTidyManager.SuppressFor(650);
                _windowManager.Apply(plan);
            }

            if (showActivity)
            {
                var message = plan.Count == 0
                    ? $"已检查 {visibleWorkingSet.Count} 个当前可见窗口，没有需要微调的地方。"
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

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
    private TidyOptions _tidyOptions;

    public NeatWinApplicationContext()
    {
        var requestedHotkey = _settingsStore.LoadHotkey();
        _tidyOptions = _settingsStore.LoadTidyOptions();

        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += RunTidy;

        _mainWindow = new MainWindow(requestedHotkey, _tidyOptions);
        _mainWindow.TidyRequested += (_, _) => RunTidy();
        _mainWindow.HotkeyChangeRequested += OnHotkeyChangeRequested;
        _mainWindow.TidyOptionsChangeRequested += OnTidyOptionsChangeRequested;
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
            _mainWindow.SetActivity("程序已运行；快捷键不可用时仍可点击“整理当前可见窗口”。", error: true);
        }

        _mainWindow.Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hotkeyWindow.HotkeyPressed -= RunTidy;
            _hotkeyWindow.Dispose();
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

        try
        {
            _settingsStore.SaveTidyOptions(_tidyOptions);
            _mainWindow.SetTidyOptionsStatus(_tidyOptions, success: true, "算法参数已保存并立即生效。");
            _mainWindow.SetActivity("算法参数已更新；下一次整理会使用新参数。");
        }
        catch (Exception exception)
        {
            _mainWindow.SetTidyOptionsStatus(
                _tidyOptions,
                success: false,
                $"参数已在本次运行中生效，但保存失败：{exception.Message}");
        }
    }

    private void RunTidy()
    {
        try
        {
            var snapshot = _windowManager.Capture();
            var visibleWorkingSet = _visibilityAnalyzer.SelectVisibleWorkingSet(snapshot);
            var plan = _tidyEngine.CreatePlan(visibleWorkingSet, _tidyOptions);
            _windowManager.Apply(plan);

            var message = plan.Count == 0
                ? $"已检查 {visibleWorkingSet.Count} 个当前可见窗口，没有需要微调的地方。"
                : $"整理完成：检查 {visibleWorkingSet.Count} 个当前可见窗口，调整 {plan.Count} 个。";
            _mainWindow.SetActivity(message);
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

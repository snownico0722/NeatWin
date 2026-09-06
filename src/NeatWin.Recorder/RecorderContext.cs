using System.Diagnostics;
using Microsoft.Win32;
using NeatWin.Core;
using NeatWin.Reference;
using NeatWin.Windows;

namespace NeatWin.Recorder;

internal sealed class RecorderContext : ApplicationContext
{
    private readonly WindowManager _manager = new();
    private readonly IntentReferenceStore _store = new();
    private readonly NativeMethods.WinEventProc _callback;
    private readonly nint _hook;
    private readonly Form _window;
    private readonly NotifyIcon _tray;
    private readonly Label _status;
    private readonly Button _pauseButton;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Dictionary<nint, int> _identities = new();
    private readonly List<Pending> _pending = [];
    private readonly string _session = Guid.NewGuid().ToString("N");
    private Active? _active;
    private bool _paused;
    private bool _disposed;
    private bool _exiting;
    private bool _handlingEvent;
    private int _sequence;
    private int _recorded;
    private int _excluded;
    private string? _error;
    private long _lastMaintenanceTick = long.MinValue;

    internal RecorderContext(bool startInTray)
    {
        _window = new Form
        {
            Text = "NeatWin 习惯记录器", StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(660, 390), MinimumSize = new Size(580, 360),
            Font = new Font("Microsoft YaHei UI", 10), AutoScaleMode = AutoScaleMode.Dpi,
        };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 5 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        panel.Controls.Add(new Label { Text = "记录动作，理解意图，不照搬布局", Dock = DockStyle.Fill,
            Font = new Font(_window.Font.FontFamily, 15, FontStyle.Bold) });
        panel.Controls.Add(new Label { Dock = DockStyle.Fill, AutoSize = false,
            Text = "可单独长期运行，不会移动任何窗口。关闭面板后留在托盘。\n\n只记录拖动／缩放前后的位置、尺寸、屏幕与邻近窗口几何。\n不记录标题、应用名、键盘内容、截图或鼠标轨迹，不联网。\n\n临时摆放、后续改动、程序介入均不是标准答案；未再调整也不代表满意。" });
        _status = new Label { Dock = DockStyle.Fill };
        panel.Controls.Add(_status);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _pauseButton = AddButton(buttons, "暂停记录", TogglePause);
        AddButton(buttons, "打开数据", OpenData);
        AddButton(buttons, "导出记录", ExportRecords);
        AddButton(buttons, "清空记录", Clear);
        panel.Controls.Add(buttons);
        var startup = new CheckBox { Text = "登录 Windows 后启动记录器（默认关闭）", Dock = DockStyle.Fill };
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            startup.Checked = key?.GetValue("NeatWin.Recorder") is string;
        }
        catch (Exception ex) { _error = ex.Message; }
        startup.CheckedChanged += (_, _) =>
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (startup.Checked) key.SetValue("NeatWin.Recorder", $"\"{Environment.ProcessPath}\" --tray");
                else key.DeleteValue("NeatWin.Recorder", throwOnMissingValue: false);
            }
            catch (Exception ex) { _error = $"开机启动设置失败：{ex.Message}"; UpdateStatus(); }
        };
        panel.Controls.Add(startup);
        _window.Controls.Add(panel);
        _window.FormClosing += (_, e) => { if (!_exiting) { e.Cancel = true; _window.Hide(); } };
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开记录器", null, (_, _) => ShowWindow());
        menu.Items.Add("暂停／继续记录", null, (_, _) => TogglePause());
        menu.Items.Add("打开数据目录", null, (_, _) => OpenData());
        menu.Items.Add("退出记录器", null, (_, _) => ExitThread());
        _tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "NeatWin 习惯记录器", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowWindow();
        // A WinForms message loop delivers out-of-context callbacks on this thread.
        // Keep the delegate rooted for the full hook lifetime. No low-level mouse/keyboard hook.
        _callback = OnWinEvent;
        _hook = NativeMethods.SetWinEventHook(NativeMethods.EventSystemMoveSizeStart,
            NativeMethods.EventSystemMoveSizeEnd, nint.Zero, _callback, 0, 0,
            NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess);
        if (_hook == nint.Zero) { _paused = true; _error = "窗口事件监听失败，未开始记录。请重新启动记录器。"; }
        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) =>
        {
            if (_lastMaintenanceTick == long.MinValue || Environment.TickCount64 - _lastMaintenanceTick >= 3_600_000)
            {
                _lastMaintenanceTick = Environment.TickCount64;
                try { _store.Prune(); }
                catch (Exception ex) { _error = $"清理旧记录失败：{ex.Message}"; UpdateStatus(); }
            }
            FlushPending();
        };
        _timer.Start();
        UpdateStatus();
        if (!startInTray || _hook == nint.Zero) _window.Show();
    }

    private static Button AddButton(Control parent, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 32, Margin = new Padding(0, 0, 8, 0) };
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
        return button;
    }

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (_disposed || _paused || _handlingEvent || hwnd == nint.Zero || objectId != 0 || childId != 0) return;
        _handlingEvent = true;
        try
        {
            if (eventType == NativeMethods.EventSystemMoveSizeStart)
            {
                // A new operation on the same window invalidates the previous provisional endpoint.
                foreach (var pending in _pending.Where(p => p.Handle == hwnd)) pending.Superseded = true;
                _active = null;
                if (AutomationGuard.IsMarked(hwnd)) { _excluded++; return; }
                var start = _manager.Capture().FirstOrDefault(w => w.Handle == hwnd && w.IsManageable);
                if (start is null) return;
                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                _active = new Active(start, pid, Environment.TickCount64);
            }
            else if (eventType == NativeMethods.EventSystemMoveSizeEnd)
            {
                var active = _active;
                _active = null;
                if (active is null || active.Window.Handle != hwnd || AutomationGuard.IsMarked(hwnd)) { _excluded++; return; }
                var captured = _manager.Capture();
                var end = captured.FirstOrDefault(w => w.Handle == hwnd && w.IsManageable);
                if (end is null || Near(active.Window.VisualRect, end.VisualRect, 3)) return;
                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid != active.ProcessId) return;
                var neighbors = new VisibilityAnalyzer().SelectVisibleWorkingSet(captured)
                    .Where(w => w.Window.Handle != hwnd && w.Window.MonitorHandle == end.MonitorHandle)
                    .Take(24).Select(w => w.Window.VisualRect).ToArray();
                if (!_identities.TryGetValue(hwnd, out var id))
                {
                    if (_identities.Count >= 256) _identities.Clear();
                    _identities[hwnd] = id = ++_sequence;
                }
                var observation = new WindowAdjustmentObservation(DateTimeOffset.UtcNow, _session, id,
                    active.Window.VisualRect, end.VisualRect, active.Window.WorkArea, end.WorkArea,
                    end.Dpi, Math.Max(0, Environment.TickCount64 - active.Tick), neighbors, "unconfirmed");
                if (_pending.Count >= 32) { _pending.RemoveAt(0); _excluded++; }
                _pending.Add(new Pending(hwnd, pid, end.MonitorHandle, observation, Environment.TickCount64));
            }
        }
        catch (Exception ex) { _error = $"捕获失败：{ex.Message}"; }
        finally { _handlingEvent = false; }
    }

    private void FlushPending()
    {
        if (_disposed || _paused || _pending.Count == 0) return;
        try
        {
            foreach (var item in _pending) item.Automated |= AutomationGuard.IsMarked(item.Handle);
            var due = _pending.Where(p => Environment.TickCount64 - p.Tick >= 2000).ToArray();
            if (due.Length == 0) return;
            var current = _manager.Capture().ToDictionary(w => w.Handle);
            foreach (var pending in due)
            {
                _pending.Remove(pending);
                var outcome = "stable";
                NativeMethods.GetWindowThreadProcessId(pending.Handle, out var pid);
                if (pending.Superseded) outcome = "superseded";
                else if (pending.Automated || AutomationGuard.IsMarked(pending.Handle)) outcome = "automation";
                else if (pid != pending.ProcessId || !current.TryGetValue(pending.Handle, out var window) || !window.IsManageable)
                    outcome = "unavailable";
                else if (window.MonitorHandle != pending.Monitor || window.WorkArea != pending.Observation.WorkArea)
                    outcome = "monitor-changed";
                else if (!Near(window.VisualRect, pending.Observation.End, 3)) outcome = "changed-after-release";
                if (pending.Observation.StartWorkArea != pending.Observation.WorkArea) outcome = "cross-monitor";
                try
                {
                    _store.Append(pending.Observation with { Outcome = outcome });
                    _recorded++;
                    _error = null;
                }
                catch (Exception ex) { _error = $"写入失败（本条未计入）：{ex.Message}"; }
            }
        }
        catch (Exception ex) { _error = $"确认窗口状态失败：{ex.Message}"; }
        UpdateStatus();
    }

    private static bool Near(RectI a, RectI b, int tolerance) =>
        Math.Abs(a.Left - b.Left) <= tolerance && Math.Abs(a.Top - b.Top) <= tolerance &&
        Math.Abs(a.Width - b.Width) <= tolerance && Math.Abs(a.Height - b.Height) <= tolerance;

    private void UpdateStatus()
    {
        var state = _paused ? "已暂停" : "正在记录";
        _status.Text = $"{state} · 本次 {_recorded} 条记录 · 跳过 {_excluded} 条\n" +
            (_error ?? "原始记录自动保留不超过 30 天／32 MB；参考只产生小幅影响。不会自动上传。");
        _pauseButton.Text = _paused ? "继续记录" : "暂停记录";
        _tray.Text = $"NeatWin 记录器 · {state}";
    }

    private void TogglePause()
    {
        if (_hook == nint.Zero) { ShowWindow(); return; }
        _paused = !_paused;
        _active = null;
        _pending.Clear();
        UpdateStatus();
    }

    private void ShowWindow() { _window.Show(); _window.WindowState = FormWindowState.Normal; _window.Activate(); }
    private void OpenData()
    {
        try { Directory.CreateDirectory(IntentReferenceStore.DefaultDirectory); Process.Start(new ProcessStartInfo(IntentReferenceStore.DefaultDirectory) { UseShellExecute = true }); }
        catch (Exception ex) { _error = ex.Message; UpdateStatus(); }
    }
    private void ExportRecords()
    {
        using var dialog = new SaveFileDialog { FileName = $"NeatWin-window-observations-{DateTime.Now:yyyyMMdd-HHmmss}.zip", Filter = "窗口几何记录（ZIP）|*.zip" };
        if (dialog.ShowDialog(_window) != DialogResult.OK) return;
        try
        {
            // SaveFileDialog explicitly confirms overwrite; stage first so a failed export does not destroy an existing file.
            var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { _store.ExportRecords(temporary); File.Move(temporary, dialog.FileName, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch (Exception ex) { _error = ex.Message; UpdateStatus(); }
    }
    private void Clear()
    {
        if (MessageBox.Show(_window, "清空本机窗口记录和弱参考？不会删除 NeatWin 的其他设置。", "清空记录",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try { _store.Clear(); _pending.Clear(); _active = null; _identities.Clear(); _recorded = 0; _error = null; }
        catch (Exception ex) { _error = ex.Message; }
        UpdateStatus();
    }
    protected override void ExitThreadCore() { _exiting = true; _window.Close(); base.ExitThreadCore(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _timer.Stop(); _timer.Dispose();
            if (_hook != nint.Zero) _ = NativeMethods.UnhookWinEvent(_hook);
            _pending.Clear();
            _tray.Visible = false; _tray.Dispose(); _window.Dispose();
        }
        base.Dispose(disposing);
    }
    private sealed record Active(WindowSnapshot Window, uint ProcessId, long Tick);
    private sealed class Pending(nint handle, uint processId, nint monitor, WindowAdjustmentObservation observation, long tick)
    {
        internal nint Handle { get; } = handle;
        internal uint ProcessId { get; } = processId;
        internal nint Monitor { get; } = monitor;
        internal WindowAdjustmentObservation Observation { get; } = observation;
        internal long Tick { get; } = tick;
        internal bool Superseded { get; set; }
        internal bool Automated { get; set; }
    }
}

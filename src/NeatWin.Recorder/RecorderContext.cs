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
    private readonly NativeMethods.WinEventProc _contextCallback;
    private readonly nint _contextHook;
    private readonly nint _foregroundHook;
    private readonly Form _window;
    private readonly NotifyIcon _tray;
    private readonly Label _status;
    private readonly Button _pauseButton;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ObservationIdentityTracker _identity = new();
    private WorkspaceFrame? _lastContext;
    private bool _contextDirty;
    private string _contextCause = "state-change-unknown";
    private int _contextRecords;
    private readonly List<Pending> _pending = [];
    private readonly string _session = Guid.NewGuid().ToString("N");
    private Active? _active;
    private bool _paused;
    private bool _disposed;
    private bool _exiting;
    private bool _handlingEvent;
    private int _recorded;
    private int _excluded;
    private string? _error;
    private long _lastMaintenanceTick = long.MinValue;

    internal RecorderContext(bool startInTray)
    {
        _window = new Form
        {
            Text = "NeatWin 习惯记录器 v2", StartPosition = FormStartPosition.CenterScreen,
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
            Text = "可单独长期运行，不会移动任何窗口。关闭面板后留在托盘。\n\n记录拖动／缩放的前后几何、层级、前台及邻居关系。\n另记前台／状态变化，来源不明的变化不用于训练；不记内容，不联网。\n\n临时摆放、后续改动、程序介入均不是标准答案；未再调整也不代表满意。" });
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
        _window.HandleCreated += (_, _) => AutomationGuard.MarkUtility(_window.Handle);
        _ = _window.Handle;
        _window.FormClosing += (_, e) => { if (!_exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; _window.Hide(); } };
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
        _contextCallback = OnContextEvent;
        _foregroundHook = NativeMethods.SetWinEventHook(0x0003, 0x0003, nint.Zero, _contextCallback, 0, 0,
            NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess);
        _contextHook = NativeMethods.SetWinEventHook(0x8000, 0x800B, nint.Zero, _contextCallback, 0, 0,
            NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess);
        try { _lastContext = _identity.Capture(_manager.Capture()); }
        catch (Exception ex) { _error = ex.Message; }
        if (_contextHook == nint.Zero || _foregroundHook == nint.Zero)
            _error = "层级／前台监听不可用，仍可记录拖动；请重启检查。";
        if (_hook == nint.Zero) { _paused = true; _error = "窗口事件监听失败，未开始记录。请重新启动记录器。"; }
        _timer = new System.Windows.Forms.Timer { Interval = 250 };
        _timer.Tick += (_, _) =>
        {
            if (_lastMaintenanceTick == long.MinValue || Environment.TickCount64 - _lastMaintenanceTick >= 3_600_000)
            {
                _lastMaintenanceTick = Environment.TickCount64;
                try { _store.Prune(); }
                catch (Exception ex) { _error = $"清理旧记录失败：{ex.Message}"; UpdateStatus(); }
            }
            FlushPending();
            CaptureContextChange();
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
                // A new operation in the workspace invalidates the previous provisional endpoint.
                foreach (var pending in _pending) pending.Superseded = true;
                _active = null;
                var snapshot = _manager.Capture();
                var start = snapshot.FirstOrDefault(w => w.Handle == hwnd && w.IsManageable);
                if (start is null) return;
                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                _active = new Active(start, pid, Environment.TickCount64, _identity.Capture(snapshot),
                    AutomationGuard.Stamp(hwnd), AutomationGuard.IsMarked(hwnd));
            }
            else if (eventType == NativeMethods.EventSystemMoveSizeEnd)
            {
                var active = _active;
                _active = null;
                if (active is null || active.Window.Handle != hwnd) { _excluded++; return; }
                var captured = _manager.Capture();
                var end = captured.FirstOrDefault(w => w.Handle == hwnd && w.IsManageable);
                if (end is null || Near(active.Window.VisualRect, end.VisualRect, 3)) return;
                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid != active.ProcessId) return;
                var neighbors = new VisibilityAnalyzer().SelectVisibleWorkingSet(captured)
                    .Where(w => w.Window.Handle != hwnd && w.Window.MonitorHandle == end.MonitorHandle)
                    .Take(24).Select(w => w.Window.VisualRect).ToArray();
                var id = _identity.Id(end);
                var context = _identity.Capture(captured);
                var observation = new WindowAdjustmentObservation(DateTimeOffset.UtcNow, _session, id,
                    active.Window.VisualRect, end.VisualRect, active.Window.WorkArea, end.WorkArea,
                    end.Dpi, Math.Max(0, Environment.TickCount64 - active.Tick), neighbors, "unconfirmed",
                    Version: 2, StartContext: active.Context, EndContext: context);
                if (_pending.Count >= 32) { _pending.RemoveAt(0); _excluded++; }
                var pending = new Pending(hwnd, pid, end.MonitorHandle, observation, Environment.TickCount64)
                {
                    Automated = active.Automated || active.AutomationStamp != AutomationGuard.Stamp(hwnd) || AutomationGuard.IsMarked(hwnd),
                    AutomationStamp = AutomationGuard.Stamp(hwnd),
                };
                _pending.Add(pending);
                _contextDirty = true;
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
            foreach (var item in _pending) item.Automated |= AutomationGuard.IsMarked(item.Handle) ||
                item.AutomationStamp != AutomationGuard.Stamp(item.Handle);
            var due = _pending.Where(p => Environment.TickCount64 - p.Tick >= 2000).ToArray();
            if (due.Length == 0) return;
            var snapshot = _manager.Capture();
            var current = snapshot.ToDictionary(w => w.Handle);
            var settledContext = _identity.Capture(snapshot);
            foreach (var pending in due)
            {
                _pending.Remove(pending);
                var outcome = "stable";
                NativeMethods.GetWindowThreadProcessId(pending.Handle, out var pid);
                if (pending.Superseded) outcome = "superseded";
                else if (pending.Automated || AutomationGuard.IsMarked(pending.Handle)) outcome = "automation";
                else if (pid != pending.ProcessId || !current.TryGetValue(pending.Handle, out var window) || !window.IsManageable)
                    outcome = "unavailable";
                else if (_identity.Id(window) != pending.Observation.WindowId) outcome = "identity-changed";
                else if (window.MonitorHandle != pending.Monitor || window.WorkArea != pending.Observation.WorkArea)
                    outcome = "monitor-changed";
                else if (!Near(window.VisualRect, pending.Observation.End, 3)) outcome = "changed-after-release";
                if (pending.Observation.StartWorkArea != pending.Observation.WorkArea) outcome = "cross-monitor";
                try
                {
                    _store.Append(pending.Observation with { Outcome = outcome, SettledContext = settledContext });
                    _recorded++;
                    _error = null;
                }
                catch (Exception ex) { _error = $"写入失败（本条未计入）：{ex.Message}"; }
            }
        }
        catch (Exception ex) { _error = $"确认窗口状态失败：{ex.Message}"; }
        UpdateStatus();
    }

    private void OnContextEvent(nint hook, uint type, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (_disposed || objectId != 0 || childId != 0) return;
        try
        {
            if (type == 0x8001) { _identity.Retire(hwnd); _contextDirty = true; return; }
            if (_paused || hwnd == nint.Zero || AutomationGuard.IsUtility(hwnd)) return;
            if (type is not 0x0003 and not 0x8002 and not 0x8003 and not 0x8004 and not 0x800B) return;
            // A notification is a dirty hint, not a claim that the user changed the Z-order.
            // No capture, disk access or accessibility inspection runs in this high-rate callback.
            _contextDirty = true;
            _contextCause = type == 0x0003 ? "foreground-observed" : "state-change-unknown";
        }
        catch { } // Never unwind through a native event callback.
    }

    private void CaptureContextChange()
    {
        if (_disposed || _paused || !_contextDirty || _active is not null) return;
        _contextDirty = false;
        try
        {
            var snapshot = _manager.Capture();
            var after = _identity.Capture(snapshot);
            if (!ObservationIdentityTracker.SameState(_lastContext, after))
            {
                var automated = snapshot.Any(w => AutomationGuard.IsMarked(w.Handle));
                _store.AppendTransition(new(2, _session, automated ? "neatwin-intervention" : _contextCause, _lastContext, after));
                _contextRecords++;
            }
            _lastContext = after;
        }
        catch (Exception ex) { _error = $"状态记录失败：{ex.Message}"; }
        UpdateStatus();
    }

    private static bool Near(RectI a, RectI b, int tolerance) =>
        Math.Abs(a.Left - b.Left) <= tolerance && Math.Abs(a.Top - b.Top) <= tolerance &&
        Math.Abs(a.Width - b.Width) <= tolerance && Math.Abs(a.Height - b.Height) <= tolerance;

    private void UpdateStatus()
    {
        var state = _paused ? "已暂停" : "正在记录";
        _status.Text = $"{state} · 本次 {_recorded} 次手势 · {_contextRecords} 次状态变化 · 跳过 {_excluded} 条\n" +
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
        _identity.Reset();
        _lastContext = null;
        _contextDirty = !_paused;
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
        try { _store.Clear(); _pending.Clear(); _active = null; _identity.Reset(); _lastContext = null; _recorded = 0; _contextRecords = 0; _error = null; }
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
            if (_contextHook != nint.Zero) _ = NativeMethods.UnhookWinEvent(_contextHook);
            if (_foregroundHook != nint.Zero) _ = NativeMethods.UnhookWinEvent(_foregroundHook);
            _pending.Clear();
            _tray.Visible = false; _tray.Dispose(); _window.Dispose();
        }
        base.Dispose(disposing);
    }
    private sealed record Active(WindowSnapshot Window, uint ProcessId, long Tick,
        WorkspaceFrame Context, nint AutomationStamp, bool Automated);
    private sealed class Pending(nint handle, uint processId, nint monitor, WindowAdjustmentObservation observation, long tick)
    {
        internal nint Handle { get; } = handle;
        internal uint ProcessId { get; } = processId;
        internal nint Monitor { get; } = monitor;
        internal WindowAdjustmentObservation Observation { get; } = observation;
        internal long Tick { get; } = tick;
        internal bool Superseded { get; set; }
        internal bool Automated { get; set; }
        internal nint AutomationStamp { get; init; }
    }
}

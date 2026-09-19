using Microsoft.Win32;
using NeatWin.Recording;
using NeatWin.Reference;
using NeatWin.Windows;

namespace NeatWin.Recorder;

internal sealed class RecorderContext : ApplicationContext
{
    private readonly RecorderSession _session = new();
    private readonly Form _window;
    private readonly NotifyIcon _tray;
    private readonly Label _status;
    private readonly Button _pauseButton;
    private bool _exiting;

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
            Text = "主程序现在已经内置同一套记录器；这个独立程序保留给兼容和单独运行。\n\n记录拖动／缩放的前后几何、层级、前台及邻居关系。\n另记前台／状态变化，来源不明的变化不用于训练；不记内容，不联网。\n\n临时摆放、后续改动、程序介入均不是标准答案；未再调整也不代表满意。" });
        _status = new Label { Dock = DockStyle.Fill };
        panel.Controls.Add(_status);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _pauseButton = AddButton(buttons, "暂停记录", _session.TogglePause);
        AddButton(buttons, "打开数据", OpenData);
        AddButton(buttons, "导出记录", ExportRecords);
        AddButton(buttons, "清空记录", Clear);
        panel.Controls.Add(buttons);
        var startup = new CheckBox { Text = "登录 Windows 后启动独立记录器（主程序已内置，通常无需开启）", Dock = DockStyle.Fill };
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            startup.Checked = key?.GetValue("NeatWin.Recorder") is string;
        }
        catch { }
        startup.CheckedChanged += (_, _) =>
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (startup.Checked) key.SetValue("NeatWin.Recorder", $"\"{Environment.ProcessPath}\" --tray");
                else key.DeleteValue("NeatWin.Recorder", throwOnMissingValue: false);
            }
            catch (Exception ex) { MessageBox.Show(_window, ex.Message, "开机启动设置失败"); }
        };
        panel.Controls.Add(startup);
        _window.Controls.Add(panel);
        _window.HandleCreated += (_, _) => AutomationGuard.MarkUtility(_window.Handle);
        _ = _window.Handle;
        _window.FormClosing += (_, e) => { if (!_exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; _window.Hide(); } };

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开记录器", null, (_, _) => ShowWindow());
        menu.Items.Add("暂停／继续记录", null, (_, _) => _session.TogglePause());
        menu.Items.Add("打开数据目录", null, (_, _) => OpenData());
        menu.Items.Add("退出记录器", null, (_, _) => ExitThread());
        _tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "NeatWin 习惯记录器", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowWindow();
        _session.StatusChanged += OnStatusChanged;
        OnStatusChanged(_session.Status);
        if (!startInTray || !_session.Status.IsAvailable) _window.Show();
    }

    private static Button AddButton(Control parent, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 32, Margin = new Padding(0, 0, 8, 0) };
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
        return button;
    }

    private void OnStatusChanged(RecorderStatus status)
    {
        _status.Text = $"{status.StateText} · 本次 {status.RecordedGestures} 次手势 · {status.ContextRecords} 次状态变化 · 跳过 {status.Excluded} 条\n" +
            (status.Error ?? "原始记录自动保留不超过 30 天／32 MB；参考只产生小幅影响。不会自动上传。");
        _pauseButton.Text = status.IsPaused ? "继续记录" : "暂停记录";
        _tray.Text = $"NeatWin 记录器 · {status.StateText}";
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.WindowState = FormWindowState.Normal;
        _window.Activate();
    }

    private void OpenData()
    {
        try { _session.OpenDataDirectory(); }
        catch (Exception ex) { MessageBox.Show(_window, ex.Message, "无法打开数据目录"); }
    }

    private void ExportRecords()
    {
        using var dialog = new SaveFileDialog { FileName = $"NeatWin-window-observations-{DateTime.Now:yyyyMMdd-HHmmss}.zip", Filter = "窗口几何记录（ZIP）|*.zip" };
        if (dialog.ShowDialog(_window) != DialogResult.OK) return;
        try { _session.ExportRecords(dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(_window, ex.Message, "导出失败"); }
    }

    private void Clear()
    {
        if (MessageBox.Show(_window, "清空本机窗口记录和弱参考？不会删除 NeatWin 的其他设置。", "清空记录",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try { _session.Clear(); }
        catch (Exception ex) { MessageBox.Show(_window, ex.Message, "清空失败"); }
    }

    protected override void ExitThreadCore()
    {
        _exiting = true;
        _window.Close();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _session.StatusChanged -= OnStatusChanged;
            _session.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _window.Dispose();
        }
        base.Dispose(disposing);
    }
}

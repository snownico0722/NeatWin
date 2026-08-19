namespace NeatWin.App;

internal sealed class MainWindow : Form
{
    private readonly Label _activityLabel;
    private readonly Label _hotkeyStatusLabel;
    private readonly CheckBox _ctrlBox;
    private readonly CheckBox _altBox;
    private readonly CheckBox _shiftBox;
    private readonly CheckBox _winBox;
    private readonly ComboBox _keyBox;
    private bool _allowClose;

    internal MainWindow(HotkeyBinding initialBinding)
    {
        Text = "NeatWin";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(520, 390);
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 7,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill };
        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "NeatWin  ·  轻轻整理当前窗口",
            Location = new Point(0, 0),
        };
        var runningLabel = new Label
        {
            AutoSize = true,
            Text = "● 正在运行",
            ForeColor = Color.ForestGreen,
            Location = new Point(0, 29),
        };
        header.Controls.Add(title);
        header.Controls.Add(runningLabel);
        root.Controls.Add(header, 0, 0);

        var tidyButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "整理当前可见窗口",
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            Margin = new Padding(0, 4, 0, 8),
        };
        tidyButton.Click += (_, _) => TidyRequested?.Invoke(this, EventArgs.Empty);
        root.Controls.Add(tidyButton, 0, 1);

        _activityLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "可直接点击上面的按钮；快捷键只是可选入口。",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        root.Controls.Add(_activityLabel, 0, 2);

        var hotkeyGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "全局快捷键",
            Padding = new Padding(12),
        };
        root.Controls.Add(hotkeyGroup, 0, 3);

        var hotkeyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 2,
        };
        hotkeyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        hotkeyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        hotkeyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        hotkeyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        hotkeyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hotkeyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        hotkeyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        hotkeyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        hotkeyGroup.Controls.Add(hotkeyLayout);

        _ctrlBox = new CheckBox { AutoSize = true, Text = "Ctrl", Anchor = AnchorStyles.Left };
        _altBox = new CheckBox { AutoSize = true, Text = "Alt", Anchor = AnchorStyles.Left };
        _shiftBox = new CheckBox { AutoSize = true, Text = "Shift", Anchor = AnchorStyles.Left };
        _winBox = new CheckBox { AutoSize = true, Text = "Win", Anchor = AnchorStyles.Left };
        hotkeyLayout.Controls.Add(_ctrlBox, 0, 0);
        hotkeyLayout.Controls.Add(_altBox, 1, 0);
        hotkeyLayout.Controls.Add(_shiftBox, 2, 0);
        hotkeyLayout.Controls.Add(_winBox, 3, 0);

        _keyBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(8),
        };
        foreach (var option in BuildKeyOptions(initialBinding.Key))
        {
            _keyBox.Items.Add(option);
        }
        hotkeyLayout.Controls.Add(_keyBox, 4, 0);

        var applyButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "应用",
            Margin = new Padding(0, 6, 0, 6),
        };
        applyButton.Click += (_, _) => ApplyHotkeyFromControls();
        hotkeyLayout.Controls.Add(applyButton, 5, 0);

        _hotkeyStatusLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        hotkeyLayout.SetColumnSpan(_hotkeyStatusLabel, 6);
        hotkeyLayout.Controls.Add(_hotkeyStatusLabel, 0, 1);

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "窗口关闭后 NeatWin 会留在系统托盘；双击托盘图标可重新打开。",
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        root.Controls.Add(hint, 0, 4);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        var exitButton = new Button
        {
            Text = "退出 NeatWin",
            AutoSize = true,
        };
        exitButton.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        bottom.Controls.Add(exitButton);
        root.Controls.Add(bottom, 0, 6);

        SetHotkeyControls(initialBinding);
        FormClosing += OnFormClosing;
    }

    internal event EventHandler? TidyRequested;
    internal event EventHandler<HotkeyChangeEventArgs>? HotkeyChangeRequested;
    internal event EventHandler? ExitRequested;

    internal void SetActivity(string message, bool error = false)
    {
        _activityLabel.Text = message;
        _activityLabel.ForeColor = error ? Color.Firebrick : SystemColors.ControlText;
    }

    internal void SetHotkeyRegistration(HotkeyBinding activeBinding, bool success, string message)
    {
        SetHotkeyControls(activeBinding);
        _hotkeyStatusLabel.Text = message;
        _hotkeyStatusLabel.ForeColor = success ? Color.ForestGreen : Color.Firebrick;
    }

    internal void BringToFrontFromTray()
    {
        if (!Visible)
        {
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        BringToFront();
    }

    internal void AllowCloseAndClose()
    {
        _allowClose = true;
        Close();
    }

    private void ApplyHotkeyFromControls()
    {
        if (_keyBox.SelectedItem is not HotkeyKeyOption option)
        {
            SetActivity("请选择一个快捷键按键。", error: true);
            return;
        }

        var binding = new HotkeyBinding(
            option.Key,
            _ctrlBox.Checked,
            _altBox.Checked,
            _shiftBox.Checked,
            _winBox.Checked);

        if (!binding.HasModifier)
        {
            _hotkeyStatusLabel.Text = "至少选择 Ctrl / Alt / Shift / Win 中的一个，避免误触。";
            _hotkeyStatusLabel.ForeColor = Color.Firebrick;
            return;
        }

        HotkeyChangeRequested?.Invoke(this, new HotkeyChangeEventArgs(binding));
    }

    private void SetHotkeyControls(HotkeyBinding binding)
    {
        _ctrlBox.Checked = binding.Control;
        _altBox.Checked = binding.Alt;
        _shiftBox.Checked = binding.Shift;
        _winBox.Checked = binding.Win;

        for (var index = 0; index < _keyBox.Items.Count; index++)
        {
            if (_keyBox.Items[index] is HotkeyKeyOption option && option.Key == binding.Key)
            {
                _keyBox.SelectedIndex = index;
                break;
            }
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose || eventArgs.CloseReason != CloseReason.UserClosing)
        {
            return;
        }

        eventArgs.Cancel = true;
        Hide();
    }

    private static IReadOnlyList<HotkeyKeyOption> BuildKeyOptions(Keys initialKey)
    {
        var keys = new List<Keys>();
        AddRange(keys, Keys.A, Keys.Z);
        AddRange(keys, Keys.D0, Keys.D9);
        AddRange(keys, Keys.F1, Keys.F12);

        if (!keys.Contains(initialKey))
        {
            keys.Add(initialKey);
        }

        return keys.Select(key => new HotkeyKeyOption(key)).ToArray();
    }

    private static void AddRange(List<Keys> destination, Keys first, Keys last)
    {
        for (var value = (int)first; value <= (int)last; value++)
        {
            destination.Add((Keys)value);
        }
    }

    private sealed record HotkeyKeyOption(Keys Key)
    {
        public override string ToString()
        {
            if (Key >= Keys.D0 && Key <= Keys.D9)
            {
                return ((char)('0' + ((int)Key - (int)Keys.D0))).ToString();
            }

            return Key.ToString();
        }
    }
}

internal sealed class HotkeyChangeEventArgs(HotkeyBinding binding) : EventArgs
{
    internal HotkeyBinding Binding { get; } = binding;
}

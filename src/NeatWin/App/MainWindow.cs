using NeatWin.Core;
using NeatWin.Recording;

namespace NeatWin.App;

internal sealed class MainWindow : Form
{
    private readonly Label _activityLabel;
    private readonly SegmentedSelector<TidyAlgorithmMode> _algorithmModeSelector;
    private readonly ComboBox _automaticMode;
    private readonly Label _automaticDescription;
    private readonly Button _tidyButton;
    private readonly Button _fullTilingButton;
    private readonly SegmentedSelector<MainSection> _sectionSelector;
    private Label _hotkeyStatusLabel = null!;
    private CheckBox _ctrlBox = null!;
    private CheckBox _altBox = null!;
    private CheckBox _shiftBox = null!;
    private CheckBox _winBox = null!;
    private ComboBox _keyBox = null!;
    private readonly TidyOptionsEditor _tidyOptionsEditor;
    private readonly Control _settingsPage;
    private readonly Control _recorderPage;
    private readonly Control _hotkeyPage;
    private Label _recorderStatusLabel = null!;
    private Button _recorderPauseButton = null!;
    private bool _suppressAutoTidyChange;
    private bool _allowClose;

    internal MainWindow(
        HotkeyBinding initialBinding,
        TidyOptions initialOptions,
        SmartBehaviorOptions initialBehaviorOptions,
        AutomaticLayoutMode automaticMode)
    {
        Text = "NeatWin";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(780, 620);
        Font = new Font("Segoe UI", 9.5F);
        BackColor = UiTheme.Page;
        ForeColor = UiTheme.Text;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 20, 22, 18),
            ColumnCount = 1,
            RowCount = 6,
            BackColor = UiTheme.Page,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        Controls.Add(root);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = UiTheme.Page,
            Margin = new Padding(0),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var headerActions = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            BackColor = UiTheme.Page,
        };

        _algorithmModeSelector = new SegmentedSelector<TidyAlgorithmMode>(
            new SegmentOption<TidyAlgorithmMode>(TidyAlgorithmMode.Smart, "Smart"),
            new SegmentOption<TidyAlgorithmMode>(TidyAlgorithmMode.Classic, "Classic"))
        {
            Width = 200,
            Margin = new Padding(0, 0, 16, 0),
        };
        _algorithmModeSelector.SetValue(initialOptions.AlgorithmMode, raiseEvent: false);
        headerActions.Controls.Add(_algorithmModeSelector);

        var autoHost = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0),
            BackColor = UiTheme.Page,
        };
        autoHost.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "自动整理",
            ForeColor = UiTheme.Text,
            Font = UiTheme.Semibold(9.5F),
            Margin = new Padding(0, 2, 9, 0),
        });
        _automaticMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 190, Margin = new Padding(0), AccessibleName = "自动整理档位",
        };
        foreach (var mode in Enum.GetValues<AutomaticLayoutMode>()) _automaticMode.Items.Add(AutomaticLayoutPolicy.Name(mode));
        _automaticMode.SelectedIndex = (int)automaticMode;
        autoHost.Controls.Add(_automaticMode);
        headerActions.Controls.Add(autoHost);

        header.Controls.Add(headerActions, 1, 0);
        _automaticDescription = new Label
        {
            Dock = DockStyle.Fill, AutoSize = true, ForeColor = UiTheme.TextMuted,
            Text = AutomaticLayoutPolicy.Description(automaticMode), Margin = new Padding(0, 4, 0, 6),
        };
        header.Controls.Add(_automaticDescription, 0, 1);
        header.SetColumnSpan(_automaticDescription, 2);
        root.Controls.Add(header, 0, 0);

        var primaryActions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 10),
            BackColor = UiTheme.Page,
        };
        primaryActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        primaryActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        _tidyButton = new Button
        {
            Dock = DockStyle.Fill, Text = "Smart 整理当前窗口", Font = UiTheme.Semibold(12F),
            Margin = new Padding(0, 0, 6, 0),
        };
        UiTheme.StylePrimary(_tidyButton);
        _tidyButton.Click += (_, _) => TidyRequested?.Invoke(this, EventArgs.Empty);
        primaryActions.Controls.Add(_tidyButton, 0, 0);
        _fullTilingButton = new Button
        {
            Dock = DockStyle.Fill, Text = "完整平铺", Font = UiTheme.Semibold(11F),
            Margin = new Padding(6, 0, 0, 0),
        };
        UiTheme.StyleSecondary(_fullTilingButton);
        _fullTilingButton.Click += (_, _) => FullTilingRequested?.Invoke(this, EventArgs.Empty);
        primaryActions.Controls.Add(_fullTilingButton, 1, 0);
        root.Controls.Add(primaryActions, 0, 1);

        _activityLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = string.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.TextMuted,
            Font = new Font("Segoe UI", 9F),
            Margin = new Padding(2, 0, 0, 0),
        };
        root.Controls.Add(_activityLabel, 0, 2);

        _sectionSelector = new SegmentedSelector<MainSection>(
            new SegmentOption<MainSection>(MainSection.Settings, "整理设置"),
            new SegmentOption<MainSection>(MainSection.Recorder, "记录器"),
            new SegmentOption<MainSection>(MainSection.Hotkey, "快捷键"))
        {
            Width = 360,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 2, 0, 6),
        };
        root.Controls.Add(_sectionSelector, 0, 3);

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Page,
            Margin = new Padding(0),
        };
        root.Controls.Add(contentHost, 0, 4);

        _tidyOptionsEditor = new TidyOptionsEditor(initialOptions, initialBehaviorOptions);
        _tidyOptionsEditor.OptionsChangeRequested += (_, eventArgs) =>
            TidyOptionsChangeRequested?.Invoke(this, eventArgs);
        _settingsPage = _tidyOptionsEditor;
        contentHost.Controls.Add(_settingsPage);

        _recorderPage = BuildRecorderPage();
        contentHost.Controls.Add(_recorderPage);

        _hotkeyPage = BuildHotkeyPage(initialBinding);
        contentHost.Controls.Add(_hotkeyPage);

        _sectionSelector.SetValue(MainSection.Settings, raiseEvent: false);
        ShowSection(MainSection.Settings);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = UiTheme.Page,
            Margin = new Padding(0, 4, 0, 0),
        };
        var exitButton = new Button
        {
            Text = "退出",
            AutoSize = true,
            Padding = new Padding(10, 3, 10, 3),
        };
        UiTheme.StyleSecondary(exitButton);
        exitButton.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        bottom.Controls.Add(exitButton);
        var undoButton = new Button { Text = "撤销上次整理", AutoSize = true, Padding = new Padding(10, 3, 10, 3) };
        UiTheme.StyleSecondary(undoButton);
        undoButton.Click += (_, _) => UndoRequested?.Invoke(this, EventArgs.Empty);
        bottom.Controls.Add(undoButton);
        root.Controls.Add(bottom, 0, 5);

        _algorithmModeSelector.ValueChanged += OnAlgorithmModeChanged;
        _sectionSelector.ValueChanged += (_, _) => ShowSection(_sectionSelector.Value);
        _automaticMode.SelectedIndexChanged += OnAutoTidyChanged;
        SetAutomaticLayoutMode(automaticMode);

        SetHotkeyControls(initialBinding);
        FormClosing += OnFormClosing;
    }

    internal event EventHandler? TidyRequested;
    internal event EventHandler? FullTilingRequested;
    internal event EventHandler? UndoRequested;
    internal event EventHandler? RecorderPauseRequested;
    internal event EventHandler? RecorderOpenDataRequested;
    internal event EventHandler? RecorderExportRequested;
    internal event EventHandler? RecorderClearRequested;
    internal event EventHandler<HotkeyChangeEventArgs>? HotkeyChangeRequested;
    internal event EventHandler<TidyOptionsChangeEventArgs>? TidyOptionsChangeRequested;
    internal event EventHandler<AutoTidyChangeEventArgs>? AutoTidyChangeRequested;
    internal event EventHandler? ExitRequested;

    internal void SetActivity(string message, bool error = false)
    {
        _activityLabel.Text = message;
        _activityLabel.ForeColor = error ? UiTheme.Danger : UiTheme.TextMuted;
    }

    internal void SetHotkeyRegistration(HotkeyBinding activeBinding, bool success, string message)
    {
        SetHotkeyControls(activeBinding);
        _hotkeyStatusLabel.Text = message;
        _hotkeyStatusLabel.ForeColor = success ? UiTheme.TextMuted : UiTheme.Danger;
    }

    internal void SetTidyOptionsStatus(
        TidyOptions activeOptions,
        SmartBehaviorOptions activeBehaviorOptions,
        bool success,
        string message)
    {
        _tidyOptionsEditor.SetStatus(activeOptions, activeBehaviorOptions, success, message);
        _algorithmModeSelector.SetValue(activeOptions.AlgorithmMode, raiseEvent: false);
    }

    internal void SetAutomaticLayoutMode(AutomaticLayoutMode mode)
    {
        _suppressAutoTidyChange = true;
        try
        {
            _automaticMode.SelectedIndex = (int)mode;
            _automaticDescription.Text = AutomaticLayoutPolicy.Description(mode);
        }
        finally
        {
            _suppressAutoTidyChange = false;
        }
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

    private void OnAlgorithmModeChanged(object? sender, EventArgs eventArgs) =>
        _tidyOptionsEditor.ChangeAlgorithmMode(_algorithmModeSelector.Value);

    private void OnAutoTidyChanged(object? sender, EventArgs eventArgs)
    {
        if (_suppressAutoTidyChange)
        {
            return;
        }

        if (_automaticMode.SelectedIndex < 0) return;
        var mode = (AutomaticLayoutMode)_automaticMode.SelectedIndex;
        SetAutomaticLayoutMode(mode);
        AutoTidyChangeRequested?.Invoke(this, new AutoTidyChangeEventArgs(mode));
    }

    private void ShowSection(MainSection section)
    {
        _settingsPage.Visible = section == MainSection.Settings;
        _recorderPage.Visible = section == MainSection.Recorder;
        _hotkeyPage.Visible = section == MainSection.Hotkey;
        (section switch
        {
            MainSection.Recorder => _recorderPage,
            MainSection.Hotkey => _hotkeyPage,
            _ => _settingsPage,
        }).BringToFront();
    }

    internal void ShowRecorderPage()
    {
        _sectionSelector.SetValue(MainSection.Recorder, raiseEvent: false);
        ShowSection(MainSection.Recorder);
        BringToFrontFromTray();
    }

    internal void SetRecorderStatus(RecorderStatus status)
    {
        _recorderStatusLabel.Text = $"{status.StateText} · 本次 {status.RecordedGestures} 次手势 · " +
            $"{status.ContextRecords} 次状态变化 · 跳过 {status.Excluded} 条\n" +
            (status.Error ?? "记录只保存在本机；不保存标题、应用名、按键、截图或鼠标轨迹。不会自动上传。");
        _recorderStatusLabel.ForeColor = status.Error is null ? UiTheme.TextMuted : UiTheme.Danger;
        _recorderPauseButton.Text = status.IsPaused ? "继续记录" : "暂停记录";
        _recorderPauseButton.Enabled = status.IsAvailable;
    }

    private Control BuildRecorderPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Page, Padding = new Padding(0, 8, 0, 0) };
        var card = new ModernCard { Dock = DockStyle.Top, Height = 230 };
        page.Controls.Add(card);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = UiTheme.Surface, Padding = new Padding(18),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        card.Controls.Add(layout);
        layout.Controls.Add(new Label
        {
            Text = "内置习惯记录器", AutoSize = true, Font = UiTheme.Semibold(11.5F), ForeColor = UiTheme.Text,
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, ForeColor = UiTheme.TextMuted,
            Text = "NeatWin 运行时自动记录窗口几何、层级、前台和拖动／缩放结果，用于后续分析操作习惯。\n" +
                   "记录器与整理器共用同一会话标记，可以区分你的操作和 NeatWin 自己的介入。",
        }, 0, 1);
        _recorderStatusLabel = new Label { Dock = DockStyle.Fill, ForeColor = UiTheme.TextMuted, TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(_recorderStatusLabel, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        _recorderPauseButton = new Button { Text = "暂停记录", AutoSize = true, Padding = new Padding(9, 3, 9, 3) };
        UiTheme.StyleSecondary(_recorderPauseButton);
        _recorderPauseButton.Click += (_, _) => RecorderPauseRequested?.Invoke(this, EventArgs.Empty);
        actions.Controls.Add(_recorderPauseButton);
        foreach (var (text, action) in new (string, Action)[]
        {
            ("打开数据", () => RecorderOpenDataRequested?.Invoke(this, EventArgs.Empty)),
            ("导出记录", () => RecorderExportRequested?.Invoke(this, EventArgs.Empty)),
            ("清空记录", () => RecorderClearRequested?.Invoke(this, EventArgs.Empty)),
        })
        {
            var button = new Button { Text = text, AutoSize = true, Padding = new Padding(9, 3, 9, 3), Margin = new Padding(8, 0, 0, 0) };
            UiTheme.StyleSecondary(button);
            button.Click += (_, _) => action();
            actions.Controls.Add(button);
        }
        layout.Controls.Add(actions, 0, 3);
        return page;
    }

    private Control BuildHotkeyPage(HotkeyBinding initialBinding)
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Page,
            Padding = new Padding(0, 8, 0, 0),
        };

        var card = new ModernCard
        {
            Dock = DockStyle.Top,
        };
        page.Controls.Add(card);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.Surface,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        card.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "全局快捷键",
            Font = UiTheme.Semibold(11.5F),
            ForeColor = UiTheme.Text,
            Anchor = AnchorStyles.Left,
        }, 0, 0);

        var hotkeyRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0),
        };
        hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 14));
        hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        layout.Controls.Add(hotkeyRow, 0, 1);

        _ctrlBox = CreateModifierChip("Ctrl");
        _altBox = CreateModifierChip("Alt");
        _shiftBox = CreateModifierChip("Shift");
        _winBox = CreateModifierChip("Win");
        hotkeyRow.Controls.Add(_ctrlBox, 0, 0);
        hotkeyRow.Controls.Add(_altBox, 1, 0);
        hotkeyRow.Controls.Add(_shiftBox, 2, 0);
        hotkeyRow.Controls.Add(_winBox, 3, 0);

        _keyBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 9, 12, 9),
        };
        UiTheme.StyleCombo(_keyBox);
        foreach (var option in BuildKeyOptions(initialBinding.Key))
        {
            _keyBox.Items.Add(option);
        }
        hotkeyRow.Controls.Add(_keyBox, 5, 0);

        var applyButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "应用",
            Margin = new Padding(0, 8, 0, 8),
        };
        UiTheme.StyleSecondary(applyButton);
        applyButton.Click += (_, _) => ApplyHotkeyFromControls();
        hotkeyRow.Controls.Add(applyButton, 6, 0);

        _hotkeyStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.TextMuted,
            Font = new Font("Segoe UI", 9F),
        };
        layout.Controls.Add(_hotkeyStatusLabel, 0, 2);

        return page;
    }

    private static CheckBox CreateModifierChip(string text)
    {
        var chip = new CheckBox
        {
            Text = text,
            Size = new Size(56, 32),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 6, 8),
        };
        UiTheme.StyleChip(chip);
        return chip;
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
            _hotkeyStatusLabel.Text = "至少选择一个修饰键。";
            _hotkeyStatusLabel.ForeColor = UiTheme.Danger;
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

    private enum MainSection
    {
        Settings,
        Recorder,
        Hotkey,
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

internal sealed class AutoTidyChangeEventArgs(AutomaticLayoutMode mode) : EventArgs
{
    internal AutomaticLayoutMode Mode { get; } = mode;
}

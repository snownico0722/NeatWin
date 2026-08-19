using NeatWin.Core;

namespace NeatWin.App;

internal sealed class TidyOptionsEditor : UserControl
{
    private readonly ComboBox _algorithmMode;
    private readonly GroupBox _smartGroup;
    private readonly ComboBox _smartStrength;
    private readonly ComboBox _smartHitTendency;
    private readonly ComboBox _smartSizeTendency;
    private readonly GroupBox _classicGroup;
    private readonly NumericUpDown _neighborSnap;
    private readonly NumericUpDown _alignmentSnap;
    private readonly NumericUpDown _screenSnap;
    private readonly NumericUpDown _maximumAdjustment;
    private readonly NumericUpDown _maximumResizePercent;
    private readonly NumericUpDown _passes;
    private readonly CheckBox _rescueOffscreen;
    private readonly Label _statusLabel;
    private TidyOptions _currentOptions;

    internal TidyOptionsEditor(TidyOptions initialOptions)
    {
        _currentOptions = initialOptions;
        Dock = DockStyle.Fill;
        Padding = new Padding(8);
        AutoScroll = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 6,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "算法模式",
            Anchor = AnchorStyles.Left,
        }, 0, 0);

        _algorithmMode = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(4),
        };
        _algorithmMode.Items.Add(new AlgorithmModeOption(TidyAlgorithmMode.Smart, "Smart · 智能整理"));
        _algorithmMode.Items.Add(new AlgorithmModeOption(TidyAlgorithmMode.Classic, "Classic · 阈值规则"));
        _algorithmMode.SelectedIndexChanged += (_, _) => UpdateModeVisibility();
        root.SetColumnSpan(_algorithmMode, 2);
        root.Controls.Add(_algorithmMode, 1, 0);

        _smartGroup = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = "Smart 偏好",
            Padding = new Padding(10),
        };
        root.SetColumnSpan(_smartGroup, 3);
        root.Controls.Add(_smartGroup, 0, 1);

        var smartLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 3,
        };
        smartLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        smartLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        smartLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        for (var row = 0; row < 3; row++)
        {
            smartLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        }
        _smartGroup.Controls.Add(smartLayout);

        _smartStrength = AddChoiceRow(
            smartLayout,
            0,
            "整理强度",
            "决定整体愿意改动多少",
            new ChoiceOption<SmartTidyStrength>(SmartTidyStrength.Gentle, "轻柔"),
            new ChoiceOption<SmartTidyStrength>(SmartTidyStrength.Balanced, "平衡"),
            new ChoiceOption<SmartTidyStrength>(SmartTidyStrength.Assertive, "积极"));

        _smartHitTendency = AddChoiceRow(
            smartLayout,
            1,
            "命中倾向",
            "决定多容易把相近窗口识别为同一组",
            new ChoiceOption<SmartHitTendency>(SmartHitTendency.Cautious, "谨慎"),
            new ChoiceOption<SmartHitTendency>(SmartHitTendency.Balanced, "平衡"),
            new ChoiceOption<SmartHitTendency>(SmartHitTendency.Sensitive, "灵敏"));

        _smartSizeTendency = AddChoiceRow(
            smartLayout,
            2,
            "尺寸倾向",
            "决定更偏向保持尺寸，还是扩大利用空白",
            new ChoiceOption<SmartSizeTendency>(SmartSizeTendency.Preserve, "保持尺寸"),
            new ChoiceOption<SmartSizeTendency>(SmartSizeTendency.Balanced, "平衡"),
            new ChoiceOption<SmartSizeTendency>(SmartSizeTendency.Expand, "扩大利用"));

        _classicGroup = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = "Classic 阈值参数",
            Padding = new Padding(10),
        };
        root.SetColumnSpan(_classicGroup, 3);
        root.Controls.Add(_classicGroup, 0, 2);

        var classicLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 6,
        };
        classicLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        classicLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        classicLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        for (var row = 0; row < 6; row++)
        {
            classicLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        }
        _classicGroup.Controls.Add(classicLayout);

        _neighborSnap = AddNumberRow(classicLayout, 0, "邻近吸合范围", 0, 240, "px", 0, 4);
        _alignmentSnap = AddNumberRow(classicLayout, 1, "边缘对齐范围", 0, 120, "px", 0, 2);
        _screenSnap = AddNumberRow(classicLayout, 2, "贴屏边范围", 0, 240, "px", 0, 4);
        _maximumAdjustment = AddNumberRow(classicLayout, 3, "最大单边调整", 0, 480, "px", 0, 8);
        _maximumResizePercent = AddNumberRow(classicLayout, 4, "最大尺寸变化", 0, 50, "%", 0, 1);
        _passes = AddNumberRow(classicLayout, 5, "迭代轮数", 1, 5, "轮", 0, 1);

        _rescueOffscreen = new CheckBox
        {
            AutoSize = true,
            Text = "把部分出屏、但当前确实可见的窗口拉回工作区",
            Anchor = AnchorStyles.Left,
        };
        root.SetColumnSpan(_rescueOffscreen, 3);
        root.Controls.Add(_rescueOffscreen, 0, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        var apply = new Button { Text = "保存并应用", AutoSize = true };
        apply.Click += (_, _) => ApplyFromControls();
        var reset = new Button { Text = "恢复默认", AutoSize = true };
        reset.Click += (_, _) => SetControls(new TidyOptions());
        buttons.Controls.Add(apply);
        buttons.Controls.Add(reset);
        root.SetColumnSpan(buttons, 3);
        root.Controls.Add(buttons, 0, 4);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Text = "Smart 只需要表达整理意图；内部权重、距离和求解轮数由 NeatWin 自动选择。切到 Classic 才会显示原始阈值参数。",
            TextAlign = ContentAlignment.TopLeft,
        };
        root.SetColumnSpan(_statusLabel, 3);
        root.Controls.Add(_statusLabel, 0, 5);

        SetControls(initialOptions);
    }

    internal event EventHandler<TidyOptionsChangeEventArgs>? OptionsChangeRequested;

    internal void SetStatus(TidyOptions activeOptions, bool success, string message)
    {
        _currentOptions = activeOptions;
        SetControls(activeOptions);
        _statusLabel.Text = message;
        _statusLabel.ForeColor = success ? Color.ForestGreen : Color.Firebrick;
    }

    private void ApplyFromControls()
    {
        var mode = _algorithmMode.SelectedItem is AlgorithmModeOption selectedMode
            ? selectedMode.Mode
            : TidyAlgorithmMode.Smart;

        var options = _currentOptions with
        {
            AlgorithmMode = mode,
            RescueOffscreenWindows = _rescueOffscreen.Checked,
        };

        if (mode == TidyAlgorithmMode.Smart)
        {
            options = options with
            {
                SmartStrength = SelectedValue(_smartStrength, SmartTidyStrength.Balanced),
                SmartHitTendency = SelectedValue(_smartHitTendency, SmartHitTendency.Balanced),
                SmartSizeTendency = SelectedValue(_smartSizeTendency, SmartSizeTendency.Balanced),
            };
        }
        else
        {
            options = options with
            {
                NeighborSnapDistance = (int)_neighborSnap.Value,
                AlignmentSnapDistance = (int)_alignmentSnap.Value,
                ScreenSnapDistance = (int)_screenSnap.Value,
                MaximumEdgeAdjustment = (int)_maximumAdjustment.Value,
                MaximumSizeChangeRatio = (double)_maximumResizePercent.Value / 100.0,
                Passes = (int)_passes.Value,
            };
        }

        OptionsChangeRequested?.Invoke(this, new TidyOptionsChangeEventArgs(options));
    }

    private void SetControls(TidyOptions options)
    {
        SelectAlgorithmMode(options.AlgorithmMode);
        SelectChoice(_smartStrength, options.SmartStrength);
        SelectChoice(_smartHitTendency, options.SmartHitTendency);
        SelectChoice(_smartSizeTendency, options.SmartSizeTendency);

        _neighborSnap.Value = ClampToDecimal(options.NeighborSnapDistance, _neighborSnap.Minimum, _neighborSnap.Maximum);
        _alignmentSnap.Value = ClampToDecimal(options.AlignmentSnapDistance, _alignmentSnap.Minimum, _alignmentSnap.Maximum);
        _screenSnap.Value = ClampToDecimal(options.ScreenSnapDistance, _screenSnap.Minimum, _screenSnap.Maximum);
        _maximumAdjustment.Value = ClampToDecimal(options.MaximumEdgeAdjustment, _maximumAdjustment.Minimum, _maximumAdjustment.Maximum);
        _maximumResizePercent.Value = ClampToDecimal(
            (decimal)(options.MaximumSizeChangeRatio * 100),
            _maximumResizePercent.Minimum,
            _maximumResizePercent.Maximum);
        _passes.Value = ClampToDecimal(options.Passes, _passes.Minimum, _passes.Maximum);
        _rescueOffscreen.Checked = options.RescueOffscreenWindows;
        UpdateModeVisibility();
    }

    private void SelectAlgorithmMode(TidyAlgorithmMode mode)
    {
        for (var index = 0; index < _algorithmMode.Items.Count; index++)
        {
            if (_algorithmMode.Items[index] is AlgorithmModeOption item && item.Mode == mode)
            {
                _algorithmMode.SelectedIndex = index;
                return;
            }
        }

        _algorithmMode.SelectedIndex = 0;
    }

    private void UpdateModeVisibility()
    {
        var smart = _algorithmMode.SelectedItem is not AlgorithmModeOption mode ||
                    mode.Mode == TidyAlgorithmMode.Smart;
        _smartGroup.Visible = smart;
        _classicGroup.Visible = !smart;
    }

    private static ComboBox AddChoiceRow<T>(
        TableLayoutPanel root,
        int row,
        string labelText,
        string explanation,
        params ChoiceOption<T>[] choices)
        where T : struct, Enum
    {
        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = labelText,
            Anchor = AnchorStyles.Left,
        }, 0, row);

        var combo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(4),
        };
        combo.Items.AddRange(choices.Cast<object>().ToArray());
        root.Controls.Add(combo, 1, row);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = explanation,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Left,
        }, 2, row);

        return combo;
    }

    private static NumericUpDown AddNumberRow(
        TableLayoutPanel root,
        int row,
        string labelText,
        decimal minimum,
        decimal maximum,
        string unit,
        int decimalPlaces,
        decimal increment)
    {
        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = labelText,
            Anchor = AnchorStyles.Left,
        }, 0, row);

        var value = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            DecimalPlaces = decimalPlaces,
            Increment = increment,
            ThousandsSeparator = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(4),
        };
        root.Controls.Add(value, 1, row);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = unit,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Left,
        }, 2, row);
        return value;
    }

    private static T SelectedValue<T>(ComboBox combo, T fallback)
        where T : struct, Enum
    {
        return combo.SelectedItem is ChoiceOption<T> option ? option.Value : fallback;
    }

    private static void SelectChoice<T>(ComboBox combo, T value)
        where T : struct, Enum
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is ChoiceOption<T> option && EqualityComparer<T>.Default.Equals(option.Value, value))
            {
                combo.SelectedIndex = index;
                return;
            }
        }

        combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
    }

    private static decimal ClampToDecimal(decimal value, decimal minimum, decimal maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));

    private sealed record AlgorithmModeOption(TidyAlgorithmMode Mode, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record ChoiceOption<T>(T Value, string Name)
        where T : struct, Enum
    {
        public override string ToString() => Name;
    }
}

internal sealed class TidyOptionsChangeEventArgs(TidyOptions options) : EventArgs
{
    internal TidyOptions Options { get; } = options;
}

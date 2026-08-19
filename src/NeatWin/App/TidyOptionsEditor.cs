using NeatWin.Core;

namespace NeatWin.App;

internal sealed class TidyOptionsEditor : UserControl
{
    private readonly ComboBox _algorithmMode;
    private readonly NumericUpDown _preserveLayoutWeight;
    private readonly NumericUpDown _resizeResistanceWeight;
    private readonly NumericUpDown _orderlinessWeight;
    private readonly NumericUpDown _spaceUsageWeight;
    private readonly NumericUpDown _smartIterations;
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
            RowCount = 15,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        for (var row = 0; row < 12; row++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        }
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        Controls.Add(root);

        var modeLabel = new Label
        {
            AutoSize = true,
            Text = "算法模式",
            Anchor = AnchorStyles.Left,
        };
        root.Controls.Add(modeLabel, 0, 0);

        _algorithmMode = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(4),
        };
        _algorithmMode.Items.Add(new AlgorithmModeOption(TidyAlgorithmMode.Smart, "Smart · 约束优化"));
        _algorithmMode.Items.Add(new AlgorithmModeOption(TidyAlgorithmMode.Classic, "Classic · 阈值规则"));
        root.SetColumnSpan(_algorithmMode, 2);
        root.Controls.Add(_algorithmMode, 1, 0);

        _preserveLayoutWeight = AddNumberRow(root, 1, "保留原布局权重", 0.1m, 5.0m, "越高越少动", 1, 0.1m);
        _resizeResistanceWeight = AddNumberRow(root, 2, "缩放阻力", 0m, 5.0m, "越高越优先整体移动", 1, 0.1m);
        _orderlinessWeight = AddNumberRow(root, 3, "整齐约束权重", 0.1m, 5.0m, "越高越愿意对齐", 1, 0.1m);
        _spaceUsageWeight = AddNumberRow(root, 4, "屏幕利用权重", 0m, 5.0m, "越高越愿意贴屏边", 1, 0.1m);
        _smartIterations = AddNumberRow(root, 5, "Smart 求解迭代", 4, 128, "轮", 0, 4);
        _neighborSnap = AddNumberRow(root, 6, "邻近关系识别范围", 0, 240, "px", 0, 4);
        _alignmentSnap = AddNumberRow(root, 7, "边缘对齐识别范围", 0, 120, "px", 0, 2);
        _screenSnap = AddNumberRow(root, 8, "屏幕边缘识别范围", 0, 240, "px", 0, 4);
        _maximumAdjustment = AddNumberRow(root, 9, "普通整理最大单边调整", 0, 480, "px", 0, 8);
        _maximumResizePercent = AddNumberRow(root, 10, "普通整理最大尺寸变化", 0, 50, "%", 0, 1);
        _passes = AddNumberRow(root, 11, "Classic 迭代轮数", 1, 5, "轮", 0, 1);

        _rescueOffscreen = new CheckBox
        {
            AutoSize = true,
            Text = "优先把部分出屏的可见窗口拉回当前显示器工作区",
            Anchor = AnchorStyles.Left,
        };
        root.SetColumnSpan(_rescueOffscreen, 3);
        root.Controls.Add(_rescueOffscreen, 0, 12);

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
        root.Controls.Add(buttons, 0, 13);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Text = "Smart 会先推断窗口关系，再做加权约束优化；Classic 保留旧版逐条规则。越界救援是硬约束。",
            TextAlign = ContentAlignment.TopLeft,
        };
        root.SetColumnSpan(_statusLabel, 3);
        root.Controls.Add(_statusLabel, 0, 14);

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
            PreserveLayoutWeight = (double)_preserveLayoutWeight.Value,
            ResizeResistanceWeight = (double)_resizeResistanceWeight.Value,
            OrderlinessWeight = (double)_orderlinessWeight.Value,
            SpaceUsageWeight = (double)_spaceUsageWeight.Value,
            SmartIterations = (int)_smartIterations.Value,
            NeighborSnapDistance = (int)_neighborSnap.Value,
            AlignmentSnapDistance = (int)_alignmentSnap.Value,
            ScreenSnapDistance = (int)_screenSnap.Value,
            MaximumEdgeAdjustment = (int)_maximumAdjustment.Value,
            MaximumSizeChangeRatio = (double)_maximumResizePercent.Value / 100.0,
            RescueOffscreenWindows = _rescueOffscreen.Checked,
            Passes = (int)_passes.Value,
        };

        OptionsChangeRequested?.Invoke(this, new TidyOptionsChangeEventArgs(options));
    }

    private void SetControls(TidyOptions options)
    {
        for (var index = 0; index < _algorithmMode.Items.Count; index++)
        {
            if (_algorithmMode.Items[index] is AlgorithmModeOption item && item.Mode == options.AlgorithmMode)
            {
                _algorithmMode.SelectedIndex = index;
                break;
            }
        }

        _preserveLayoutWeight.Value = ClampToDecimal((decimal)options.PreserveLayoutWeight, _preserveLayoutWeight.Minimum, _preserveLayoutWeight.Maximum);
        _resizeResistanceWeight.Value = ClampToDecimal((decimal)options.ResizeResistanceWeight, _resizeResistanceWeight.Minimum, _resizeResistanceWeight.Maximum);
        _orderlinessWeight.Value = ClampToDecimal((decimal)options.OrderlinessWeight, _orderlinessWeight.Minimum, _orderlinessWeight.Maximum);
        _spaceUsageWeight.Value = ClampToDecimal((decimal)options.SpaceUsageWeight, _spaceUsageWeight.Minimum, _spaceUsageWeight.Maximum);
        _smartIterations.Value = ClampToDecimal(options.SmartIterations, _smartIterations.Minimum, _smartIterations.Maximum);
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
        var label = new Label
        {
            AutoSize = true,
            Text = labelText,
            Anchor = AnchorStyles.Left,
        };
        root.Controls.Add(label, 0, row);

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

        var unitLabel = new Label
        {
            AutoSize = true,
            Text = unit,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Left,
        };
        root.Controls.Add(unitLabel, 2, row);
        return value;
    }

    private static decimal ClampToDecimal(decimal value, decimal minimum, decimal maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));

    private sealed record AlgorithmModeOption(TidyAlgorithmMode Mode, string Name)
    {
        public override string ToString() => Name;
    }
}

internal sealed class TidyOptionsChangeEventArgs(TidyOptions options) : EventArgs
{
    internal TidyOptions Options { get; } = options;
}

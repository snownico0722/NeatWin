using NeatWin.Core;

namespace NeatWin.App;

internal sealed class TidyOptionsEditor : UserControl
{
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
        Padding = new Padding(10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 9,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        for (var row = 0; row < 6; row++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        }
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        _neighborSnap = AddNumberRow(root, 0, "邻近吸合范围", 0, 240, "px");
        _alignmentSnap = AddNumberRow(root, 1, "边缘对齐范围", 0, 120, "px");
        _screenSnap = AddNumberRow(root, 2, "贴屏边范围", 0, 240, "px");
        _maximumAdjustment = AddNumberRow(root, 3, "普通整理最大单边调整", 0, 480, "px");
        _maximumResizePercent = AddNumberRow(root, 4, "普通整理最大尺寸变化", 0, 50, "%");
        _passes = AddNumberRow(root, 5, "算法迭代轮数", 1, 5, "轮");

        _rescueOffscreen = new CheckBox
        {
            AutoSize = true,
            Text = "优先把部分出屏的可见窗口拉回当前显示器工作区",
            Anchor = AnchorStyles.Left,
        };
        root.SetColumnSpan(_rescueOffscreen, 3);
        root.Controls.Add(_rescueOffscreen, 0, 6);

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
        root.Controls.Add(buttons, 0, 7);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Text = "这些参数只约束“微调”力度；越界救援开启时可突破普通移动预算。",
            TextAlign = ContentAlignment.TopLeft,
        };
        root.SetColumnSpan(_statusLabel, 3);
        root.Controls.Add(_statusLabel, 0, 8);

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
        var options = _currentOptions with
        {
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
        string unit)
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
            DecimalPlaces = 0,
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
}

internal sealed class TidyOptionsChangeEventArgs(TidyOptions options) : EventArgs
{
    internal TidyOptions Options { get; } = options;
}

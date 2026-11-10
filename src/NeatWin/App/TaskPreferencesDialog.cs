using System.Windows;
using System.Windows.Controls;
using NeatWin.Core;
using NeatWin.Presentation;

namespace NeatWin.App;

internal sealed class TaskPreferencesDialog : Window
{
    private readonly CheckBox _resize = new() { Content = "允许为更好的使用效果调整大小" };
    private readonly CheckBox _bleed = new() { Content = "允许少量水平出屏" };
    private readonly CheckBox _protect = new() { Content = "保护四侧边缘" };
    private readonly CheckBox _physical = new() { Content = "提供当前工作区的物理观看参数" };
    private readonly IntegerInput _bleedSize = new() { Minimum = 0, Maximum = 48 };
    private readonly IntegerInput _width = new() { Minimum = 480, Maximum = 2400, Step = 20 };
    private readonly IntegerInput _height = new() { Minimum = 320, Maximum = 1600, Step = 20 };
    private readonly IntegerInput _physicalWidth = new() { Minimum = 150, Maximum = 4000, Step = 10 };
    private readonly IntegerInput _distance = new() { Minimum = 200, Maximum = 3000, Step = 10 };
    private readonly TaskPreferences _initial;
    private readonly RectI _area;
    private readonly uint _dpi;
    internal WindowTheme Theme { get; }
    internal TaskPreferences Value => new(_initial.Profile with
    {
        AllowUsefulResize = _resize.IsChecked == true, AllowPeripheralBleed = _bleed.IsChecked == true,
        MaximumBleedDip = _bleedSize.Value, ComfortableWidthDip = _width.Value,
        ComfortableHeightDip = _height.Value, ProtectWindowEdges = _protect.IsChecked == true,
    }, _physical.IsChecked == true ? new(_area, _dpi, _physicalWidth.Value, _distance.Value) : null);

    internal TaskPreferencesDialog(TaskPreferences preferences, RectI area, uint dpi)
    {
        _initial = preferences; _area = area; _dpi = dpi;
        Title = "排布偏好"; Width = 680; Height = 720; MinWidth = 580; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/NeatWin;component/Presentation/Design.xaml", UriKind.Relative) });
        Theme = new WindowTheme(this);
        var outer = new DockPanel { Margin = new Thickness(24) }; Content = outer;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,16,0,0) };
        DockPanel.SetDock(actions, Dock.Bottom); outer.Children.Add(actions);
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0,0,8,0) };
        var save = new Button { Content = "保存偏好", IsDefault = true, Style = (Style)FindResource("PrimaryButton") };
        // Move keyboard focus first so an in-progress number edit is committed before reading Value.
        save.Click += (_, _) => { save.Focus(); DialogResult = true; };
        actions.Children.Add(cancel); actions.Children.Add(save);
        var panel = new StackPanel(); outer.Children.Add(new ScrollViewer { Content = panel, Padding = new Thickness(0,0,12,0) });
        panel.Children.Add(new TextBlock { Text = "让整理更符合你的习惯", Style = (Style)FindResource("SectionTitle"), Margin = new Thickness(0,0,0,8) });
        panel.Children.Add(new TextBlock { Text = "这些是偏好，不是必须选择的工作模式。", Style = (Style)FindResource("MutedText"), Margin = new Thickness(0,0,0,18) });
        void Add(FrameworkElement content) { content.Margin = new Thickness(0,0,0,14); panel.Children.Add(content); }
        void Row(string title, FrameworkElement input)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
            var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,16,0) };
            row.Children.Add(label); Grid.SetColumn(input, 1); row.Children.Add(input); Add(row);
        }
        _resize.IsChecked = preferences.Profile.AllowUsefulResize; Add(_resize);
        _bleed.IsChecked = preferences.Profile.AllowPeripheralBleed; Add(_bleed);
        _protect.IsChecked = preferences.Profile.ProtectWindowEdges; Add(_protect);
        _bleedSize.Value = (int)Math.Round(preferences.Profile.MaximumBleedDip); Row("最大水平出屏量（DIP）", _bleedSize);
        _width.Value = (int)Math.Round(preferences.Profile.ComfortableWidthDip); Row("共同观看的参考宽度（DIP）", _width);
        _height.Value = (int)Math.Round(preferences.Profile.ComfortableHeightDip); Row("共同观看的参考高度（DIP）", _height);
        _physical.IsChecked = preferences.Calibration?.Matches(area, dpi) == true; Add(_physical);
        _physicalWidth.Value = (int)Math.Round(_physical.IsChecked == true ? preferences.Calibration!.WidthMillimeters : 600);
        _distance.Value = (int)Math.Round(_physical.IsChecked == true ? preferences.Calibration!.DistanceMillimeters : 700);
        Row("可用工作区实际宽度（毫米）", _physicalWidth); Row("眼睛到屏幕中心距离（毫米）", _distance);
        Add(new TextBlock { Text = $"对应工作区：{area.Width} × {area.Height}，DPI {dpi}。\n未提供物理参数时使用归一化距离；这不是眼动追踪。", Style = (Style)FindResource("MutedText") });
        void EnableInputs()
        {
            _physicalWidth.IsEnabled = _distance.IsEnabled = _physical.IsChecked == true;
            _bleed.IsEnabled = _protect.IsChecked != true;
            _bleedSize.IsEnabled = _bleed.IsChecked == true && _protect.IsChecked != true;
        }
        foreach (var check in new[] { _protect, _physical, _bleed })
        { check.Checked += (_, _) => EnableInputs(); check.Unchecked += (_, _) => EnableInputs(); }
        EnableInputs();
    }
}

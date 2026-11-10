using System.ComponentModel;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using NeatWin.Core;
using NeatWin.Presentation;
using NeatWin.Recording;

namespace NeatWin.App;

public partial class MainWindow : Window
{
    private bool _ready, _suppressAutomation, _allowClose;
    internal WindowTheme Theme { get; }
    internal event EventHandler? TidyRequested, UndoRequested, ExitRequested, PreferencesRequested;
    internal event EventHandler? RecorderPauseRequested, RecorderOpenDataRequested, RecorderExportRequested, RecorderClearRequested;
    internal event EventHandler<HotkeyChangeEventArgs>? HotkeyChangeRequested;
    internal event EventHandler<TidyOptionsChangeEventArgs>? TidyOptionsChangeRequested;
    internal event EventHandler<AutoTidyChangeEventArgs>? AutoTidyChangeRequested;

    internal MainWindow(HotkeyBinding binding, TidyOptions options, SmartBehaviorOptions behavior, AutomaticLayoutMode mode)
    {
        InitializeComponent();
        Theme = new WindowTheme(this);
        using (var identity = WindowsIdentity.GetCurrent())
            ElevationLabel.Text = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
                ? "管理员运行" : "部分窗口需要管理员权限";
        HotkeySelector.ItemsSource = Enumerable.Range(0, 256).Where(HotkeyBinding.IsValidKey)
            .Select(code => new KeyOption(code, HotkeyBinding.FormatKey(code))).ToArray();
        SetHotkeyControls(binding);
        OptionsEditor.Initialize(options, behavior);
        OptionsEditor.OptionsChangeRequested += (_, e) => TidyOptionsChangeRequested?.Invoke(this, e);
        SmartAlgorithm.IsChecked = options.AlgorithmMode == TidyAlgorithmMode.Smart;
        ClassicAlgorithm.IsChecked = options.AlgorithmMode == TidyAlgorithmMode.Classic;
        SetAutomaticLayoutMode(mode);
        _ready = true;
    }
    internal void SetActivity(string message, bool error = false)
    {
        ActivityLabel.Text = message;
        ActivityLabel.SetResourceReference(ForegroundProperty, error ? "DangerBrush" : "MutedBrush");
    }
    internal void SetHotkeyRegistration(HotkeyBinding binding, bool success, string message)
    {
        SetHotkeyControls(binding); HotkeyStatus.Text = message;
        HotkeyStatus.SetResourceReference(ForegroundProperty, success ? "MutedBrush" : "DangerBrush");
    }
    private void SetHotkeyControls(HotkeyBinding binding)
    {
        ControlKey.IsChecked = binding.Control; AltKey.IsChecked = binding.Alt;
        ShiftKey.IsChecked = binding.Shift; WinKey.IsChecked = binding.Win;
        HotkeySelector.SelectedValue = binding.Key;
    }
    internal void SetTidyOptionsStatus(TidyOptions options, SmartBehaviorOptions behavior, bool success, string message) =>
        OptionsEditor.SetStatus(options, behavior, success, message);
    internal void SetAutomaticLayoutMode(AutomaticLayoutMode mode)
    {
        _suppressAutomation = true;
        new[] { AutoOff, AutoLight, AutoFull, AutoWorkspace }[(int)mode].IsChecked = true;
        AutomationDescription.Text = AutomaticLayoutPolicy.Description(mode);
        _suppressAutomation = false;
    }
    internal void SetRecorderStatus(RecorderStatus status)
    {
        RecorderState.Text = status.StateText;
        RecorderCounts.Text = $"拖动记录 {status.RecordedGestures} · 工作区记录 {status.ContextRecords} · 已排除 {status.Excluded}";
        RecorderError.Text = status.Error ?? string.Empty;
        RecorderPauseButton.Content = status.IsPaused ? "继续记录" : "暂停记录";
        RecorderPauseButton.IsEnabled = status.IsAvailable;
    }
    internal void BringToFrontFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
    internal void PrepareShutdown() => _allowClose = true;
    internal void AllowCloseAndClose() { _allowClose = true; Close(); }
    internal void ShowRecorderPage() { RecorderNavigation.IsChecked = true; ShowPage("recorder"); BringToFrontFromTray(); }
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && !Program.IsUiSmoke) { e.Cancel = true; Hide(); }
    }
    private void Navigate(object sender, RoutedEventArgs e) { if (_ready) ShowPage((string)((RadioButton)sender).Tag); }
    private void ShowPage(string page)
    {
        LayoutPage.Visibility = page == "layout" ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsPage.Visibility = page == "shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        RecorderPage.Visibility = page == "recorder" ? Visibility.Visible : Visibility.Collapsed;
    }
    private void AutomationChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _suppressAutomation) return;
        var mode = (AutomaticLayoutMode)int.Parse((string)((RadioButton)sender).Tag);
        AutomationDescription.Text = AutomaticLayoutPolicy.Description(mode);
        AutoTidyChangeRequested?.Invoke(this, new(mode));
    }
    private void AlgorithmChanged(object sender, RoutedEventArgs e)
    {
        if (_ready) OptionsEditor.ChangeAlgorithmMode((TidyAlgorithmMode)int.Parse((string)((RadioButton)sender).Tag));
    }
    private void ApplyHotkey(object sender, RoutedEventArgs e)
    {
        if (HotkeySelector.SelectedValue is not int code) return;
        var binding = new HotkeyBinding(code, ControlKey.IsChecked == true, AltKey.IsChecked == true,
            ShiftKey.IsChecked == true, WinKey.IsChecked == true);
        if (!binding.HasModifier) { HotkeyStatus.Text = "请至少选择一个修饰键。"; return; }
        HotkeyChangeRequested?.Invoke(this, new(binding));
    }
    private void ToggleTheme(object sender, RoutedEventArgs e) => Theme.Toggle();
    private void PreferencesClick(object sender, RoutedEventArgs e) => PreferencesRequested?.Invoke(this, EventArgs.Empty);
    private void ExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
    private void TidyClick(object sender, RoutedEventArgs e) => TidyRequested?.Invoke(this, EventArgs.Empty);
    private void UndoClick(object sender, RoutedEventArgs e) => UndoRequested?.Invoke(this, EventArgs.Empty);
    private void RecorderPauseClick(object sender, RoutedEventArgs e) => RecorderPauseRequested?.Invoke(this, EventArgs.Empty);
    private void RecorderExportClick(object sender, RoutedEventArgs e) => RecorderExportRequested?.Invoke(this, EventArgs.Empty);
    private void RecorderOpenClick(object sender, RoutedEventArgs e) => RecorderOpenDataRequested?.Invoke(this, EventArgs.Empty);
    private void RecorderClearClick(object sender, RoutedEventArgs e) => RecorderClearRequested?.Invoke(this, EventArgs.Empty);
    private sealed record KeyOption(int Code, string Label);
}

internal sealed class HotkeyChangeEventArgs(HotkeyBinding binding) : EventArgs { internal HotkeyBinding Binding { get; } = binding; }
internal sealed class AutoTidyChangeEventArgs(AutomaticLayoutMode mode) : EventArgs { internal AutomaticLayoutMode Mode { get; } = mode; }

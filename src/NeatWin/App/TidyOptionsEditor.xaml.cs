using System.Windows;
using System.Windows.Controls;
using NeatWin.Core;

namespace NeatWin.App;

public partial class TidyOptionsEditor : UserControl
{
    private bool _ready, _setting;
    private TidyOptions _options = new();
    private SmartBehaviorOptions _behavior = new();
    internal event EventHandler<TidyOptionsChangeEventArgs>? OptionsChangeRequested;
    public TidyOptionsEditor() { InitializeComponent(); }
    internal void Initialize(TidyOptions options, SmartBehaviorOptions behavior)
    { SetStatus(options, behavior, true, string.Empty); _ready = true; }
    internal void SetStatus(TidyOptions options, SmartBehaviorOptions behavior, bool success, string message)
    {
        _setting = true; _options = options; _behavior = behavior;
        GentleStrength.IsChecked = options.SmartStrength == SmartTidyStrength.Gentle;
        BalancedStrength.IsChecked = options.SmartStrength == SmartTidyStrength.Balanced;
        AssertiveStrength.IsChecked = options.SmartStrength == SmartTidyStrength.Assertive;
        VerticalFill.IsChecked = behavior.PreferReversibleVerticalFill;
        VideoBars.IsChecked = behavior.RemoveVideoBlackBars;
        ShrinkVideo.IsChecked = behavior.VideoBlackBarTendency == VideoBlackBarTendency.Shrink;
        ExpandVideo.IsChecked = behavior.VideoBlackBarTendency == VideoBlackBarTendency.Expand;
        NeighborSnap.Value = options.NeighborSnapDistance; AlignmentSnap.Value = options.AlignmentSnapDistance;
        ScreenSnap.Value = options.ScreenSnapDistance; MaximumAdjustment.Value = options.MaximumEdgeAdjustment;
        MaximumResize.Value = (int)Math.Round(options.MaximumSizeChangeRatio * 100); Passes.Value = options.Passes;
        RescueOffscreen.IsChecked = options.RescueOffscreenWindows;
        OptionsStatus.Text = success ? string.Empty : message;
        UpdateVisibility(); _setting = false;
    }
    internal void ChangeAlgorithmMode(TidyAlgorithmMode mode)
    { _options = _options with { AlgorithmMode = mode }; UpdateVisibility(); Apply(); }
    private void UpdateVisibility()
    {
        SmartCard.Visibility = _options.AlgorithmMode == TidyAlgorithmMode.Smart ? Visibility.Visible : Visibility.Collapsed;
        ClassicCard.Visibility = _options.AlgorithmMode == TidyAlgorithmMode.Classic ? Visibility.Visible : Visibility.Collapsed;
        VideoTendencyRow.Visibility = VideoBars.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Changed(object sender, RoutedEventArgs e) { if (_ready && !_setting) Apply(); }
    private void ValueChanged(object? sender, EventArgs e) { if (_ready && !_setting) Apply(); }
    private void Apply()
    {
        if (!_ready || _setting) return;
        _options = _options with { RescueOffscreenWindows = RescueOffscreen.IsChecked == true };
        if (_options.AlgorithmMode == TidyAlgorithmMode.Smart)
        {
            _options = _options with
            {
                SmartStrength = GentleStrength.IsChecked == true ? SmartTidyStrength.Gentle :
                    AssertiveStrength.IsChecked == true ? SmartTidyStrength.Assertive : SmartTidyStrength.Balanced,
                SmartHitTendency = SmartHitTendency.Balanced, SmartSizeTendency = SmartSizeTendency.Balanced,
            };
            _behavior = _behavior with
            {
                PreferReversibleVerticalFill = VerticalFill.IsChecked == true,
                RemoveVideoBlackBars = VideoBars.IsChecked == true,
                VideoBlackBarTendency = ExpandVideo.IsChecked == true ? VideoBlackBarTendency.Expand : VideoBlackBarTendency.Shrink,
                OverlapAvoidance = SmartOverlapAvoidance.Balanced,
            };
        }
        else _options = _options with
        {
            NeighborSnapDistance = NeighborSnap.Value, AlignmentSnapDistance = AlignmentSnap.Value,
            ScreenSnapDistance = ScreenSnap.Value, MaximumEdgeAdjustment = MaximumAdjustment.Value,
            MaximumSizeChangeRatio = MaximumResize.Value / 100d, Passes = Passes.Value,
        };
        OptionsStatus.Text = string.Empty; UpdateVisibility();
        OptionsChangeRequested?.Invoke(this, new(_options, _behavior));
    }
    private void ResetDefaults(object sender, RoutedEventArgs e)
    { SetStatus(new TidyOptions(AlgorithmMode: _options.AlgorithmMode), new(), true, string.Empty); Apply(); }
}
internal sealed class TidyOptionsChangeEventArgs(TidyOptions options, SmartBehaviorOptions behaviorOptions) : EventArgs
{
    internal TidyOptions Options { get; } = options;
    internal SmartBehaviorOptions BehaviorOptions { get; } = behaviorOptions;
}

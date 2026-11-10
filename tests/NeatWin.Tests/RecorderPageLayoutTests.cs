using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NeatWin.App;
using NeatWin.Core;
using NeatWin.Recording;

namespace NeatWin.Tests;

public sealed class RecorderPageLayoutTests
{
    [Theory]
    [InlineData(1000, 760)]
    [InlineData(840, 620)]
    public void RecorderActionsRemainVisibleAndClickable(double width, double height)
    {
        OnSta(() =>
        {
            var window = new MainWindow(HotkeyBinding.Default, new(), new(), AutomaticLayoutMode.Off) { Width = width, Height = height };
            try
            {
                window.ShowRecorderPage(); window.SetRecorderStatus(new RecorderStatus(false, true, 7, 11, 1, null));
                window.UpdateLayout(); Pump();
                var pause = 0; var export = 0;
                window.RecorderPauseRequested += (_, _) => pause++;
                window.RecorderExportRequested += (_, _) => export++;
                foreach (var name in new[] { "RecorderPauseButton", "RecorderOpenButton", "RecorderExportButton", "RecorderClearButton" })
                {
                    var button = Assert.IsType<Button>(window.FindName(name));
                    Assert.True(button.IsVisible && button.ActualWidth > 0 && button.ActualHeight > 0);
                    var rect = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
                    Assert.True(rect.Left >= 0 && rect.Top >= 0 && rect.Right <= window.ActualWidth && rect.Bottom <= window.ActualHeight);
                    var presenter = Descendants(button).OfType<ContentPresenter>().First();
                    Assert.True(presenter.ActualWidth >= 35, "Button label has no usable space.");
                }
                ((Button)window.FindName("RecorderPauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Button)window.FindName("RecorderExportButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, pause); Assert.Equal(1, export);
            }
            finally { window.AllowCloseAndClose(); }
        });
    }
    [Fact]
    public void FourAutomationChoicesAndNativeVirtualKeyBindingArePreserved()
    {
        OnSta(() =>
        {
            var window = new MainWindow(HotkeyBinding.Default, new(), new(), AutomaticLayoutMode.Off);
            try
            {
                window.Show(); window.UpdateLayout(); Pump();
                var choices = Descendants(window).OfType<RadioButton>().Where(b => b.GroupName == "Automation").ToArray();
                Assert.Equal(4, choices.Length);
                var changed = new List<AutomaticLayoutMode>();
                window.AutoTidyChangeRequested += (_, e) => changed.Add(e.Mode);
                foreach (var choice in choices.OrderBy(b => int.Parse((string)b.Tag))) choice.IsChecked = true;
                Assert.Equal(new[] { AutomaticLayoutMode.LightAssist, AutomaticLayoutMode.FullAssist, AutomaticLayoutMode.AutoFullAssist }, changed);
                Assert.Equal(0x54, HotkeyBinding.Default.Key);
                Assert.Equal("T", HotkeyBinding.FormatKey(0x54));
                Assert.Equal("F12", HotkeyBinding.FormatKey(0x7B));
            }
            finally { window.AllowCloseAndClose(); }
        });
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception error) { failure = error; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(15000), "WPF UI check did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using NeatWin.App;
using NeatWin.Core;
using NeatWin.Recording;

namespace NeatWin.Tests;

public sealed class RecorderPageLayoutTests
{
    [Fact]
    public void RecorderControlsAreInsideVisibleParentBoundsAndRemainClickable()
    {
        Exception? failure = null;
        var ui = new Thread(() =>
        {
            try
            {
                using var form = new MainWindow(HotkeyBinding.Default, new(), new(), AutomaticLayoutMode.Off);
                form.ShowRecorderPage();
                form.SetRecorderStatus(new RecorderStatus(false, true, 7, 11, 1, null));
                form.PerformLayout();
                Application.DoEvents();
                var title = Descendants(form).OfType<Label>().Single(c => c.Text == "内置习惯记录器");
                AssertUnclipped(title);
                var pauseCalls = 0; var exportCalls = 0;
                form.RecorderPauseRequested += (_, _) => pauseCalls++;
                form.RecorderExportRequested += (_, _) => exportCalls++;
                foreach (var name in new[] { "暂停记录", "打开数据", "导出记录", "清空记录" })
                {
                    var button = Descendants(form).OfType<Button>().Single(c => c.Text == name);
                    AssertUnclipped(button);
                    Assert.True(button.ClientSize.Width >= TextRenderer.MeasureText(button.Text, button.Font).Width);
                }
                Descendants(form).OfType<Button>().Single(c => c.Text == "暂停记录").PerformClick();
                Descendants(form).OfType<Button>().Single(c => c.Text == "导出记录").PerformClick();
                Assert.Equal(1, pauseCalls);
                Assert.Equal(1, exportCalls);
                form.AllowCloseAndClose();
            }
            catch (Exception e) { failure = e; }
        }) { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        Assert.True(ui.Join(15000), "Recorder page layout did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void AssertUnclipped(Control control)
    {
        Assert.True(control.Visible && control.Width > 0 && control.Height > 0);
        var bounds = control.RectangleToScreen(control.ClientRectangle);
        var clipped = bounds;
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
            clipped = Rectangle.Intersect(clipped, parent.RectangleToScreen(parent.ClientRectangle));
        Assert.Equal(bounds, clipped);
    }

    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
        .SelectMany(c => new[] { c }.Concat(Descendants(c)));
}

using System.Windows;
using NeatWin.App;
using NeatWin.Core;
using NeatWin.Presentation;

namespace NeatWin;

internal static class Program
{
    internal static bool IsUiSmoke { get; private set; }
    [STAThread]
    private static int Main(string[] args)
    {
        IsUiSmoke = args.Length == 2 && args[0] == "--ui-smoke";
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        NeatWinApplicationHost? host = null;
        app.SessionEnding += (_, _) => host?.PrepareShutdown();
        app.Exit += (_, _) => host?.Dispose();
        app.Startup += async (_, _) =>
        {
            try
            {
                if (IsUiSmoke)
                {
                    var window = new MainWindow(HotkeyBinding.Default, new(), new(), AutomaticLayoutMode.Off);
                    window.Show();
                    await WindowTheme.CaptureAsync(window, window.Theme, args[1]);
                    window.ShowRecorderPage();
                    window.SetRecorderStatus(new(false, true, 7, 11, 1, null));
                    await WindowTheme.CaptureAsync(window, window.Theme, Path.Combine(args[1], "recorder"));
                    window.AllowCloseAndClose(); app.Shutdown(0);
                }
                else host = new NeatWinApplicationHost();
            }
            catch (Exception ex)
            {
                if (IsUiSmoke)
                { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "error.txt"), ex.ToString()); }
                else MessageBox.Show(ex.Message, "NeatWin 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                app.Shutdown(1);
            }
        };
        return app.Run();
    }
}

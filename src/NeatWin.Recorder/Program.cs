namespace NeatWin.Recorder;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var context = new RecorderContext(args.Contains("--tray"));
        Application.Run(context);
    }
}

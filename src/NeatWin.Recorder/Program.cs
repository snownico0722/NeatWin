namespace NeatWin.Recorder;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var singleton = new Mutex(true, @"Local\NeatWin.Recorder.v1", out var created);
        if (!created)
        {
            MessageBox.Show("记录器已经在运行，请从系统托盘打开。", "NeatWin 习惯记录器");
            return;
        }
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var context = new RecorderContext(args.Contains("--tray"));
            Application.Run(context);
        }
        finally { singleton.ReleaseMutex(); }
    }
}

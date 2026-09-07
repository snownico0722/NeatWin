using System.Diagnostics;
using System.Runtime.InteropServices;
using NeatWin.Core;
using NeatWin.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        if (args.Length > 0 && args[0] == "--child")
        {
            using var form = new Form { Text = "NeatWin synthetic smoke window", Size = new Size(500, 400),
                StartPosition = FormStartPosition.Manual, Location = new Point(80 + int.Parse(args[1]) * 90, 80) };
            Application.Run(form);
            return 0;
        }
        var children = new List<Process>();
        try
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "NeatWin.DesktopSmoke.exe");
            for (var i = 0; i < 3; i++)
            {
                var child = Process.Start(new ProcessStartInfo(exe, $"--child {i}") { UseShellExecute = false })
                    ?? throw new InvalidOperationException("Child did not start.");
                children.Add(child);
                Require(child.WaitForInputIdle(15000), "Child did not reach its message loop.");
            }
            var manager = new WindowManager();
            var ids = children.Select(p => (uint)p.Id).ToHashSet();
            var watch = Stopwatch.StartNew();
            WindowSnapshot[] windows;
            do
            {
                Thread.Sleep(100);
                windows = manager.Capture().Where(w => ids.Contains(w.ProcessId)).ToArray();
            } while (windows.Length != 3 && watch.ElapsedMilliseconds < 5000);
            Require(windows.Length == 3, "Could not capture all three synthetic windows.");
            // Only rearrange children created by this test; unrelated desktop windows are untouched.
            for (var i = 1; i < windows.Length; i++)
                Require(SetWindowPos(windows[i].Handle, windows[i - 1].Handle, 0, 0, 0, 0, 0x213), "Initial order setup failed.");
            windows = manager.Capture().Where(w => ids.Contains(w.ProcessId)).OrderBy(w => w.ZOrder).ToArray();
            Require(WindowLayerSafety.CanReorder(windows, manager.Capture()), "Synthetic block is not contiguous.");
            var foreground = GetForegroundWindow();
            Require(foreground != nint.Zero, "No foreground window exists on this test desktop.");
            var reverse = new WindowLayerOrder(Enumerable.Reverse(windows).ToArray());
            var applied = manager.ApplyLayout(new([], [reverse], []));
            var actual = manager.Capture();
            Require(applied.Plan.Layers.Count == 1 && WindowLayerSafety.MatchesOrder(reverse, actual), "Relative reversal failed.");
            Require(GetForegroundWindow() == foreground, "Restacking stole foreground focus.");
            Require(actual.Where(w => ids.Contains(w.ProcessId)).All(w => !w.IsTopmost), "Restacking changed topmost state.");
            Require(windows.All(w => actual.Any(a => a.Handle == w.Handle && a.VisualRect == w.VisualRect)), "Layer-only operation changed geometry.");
            Console.WriteLine("PASS: reversed an ordinary three-window block without focus, geometry or topmost changes.");
            manager.ApplyLayout(new([], [new(windows)], []));
            Require(WindowLayerSafety.MatchesOrder(new(windows), manager.Capture()), "Layer restore failed.");
            Console.WriteLine("PASS: original relative order restored.");
            var first = manager.Capture().First(w => w.Handle == windows[0].Handle);
            Require(SetWindowPos(first.Handle, (nint)(-1), 0, 0, 0, 0, 0x213), "Synthetic topmost setup failed.");
            var rejected = manager.ApplyLayout(new([], [reverse], []));
            Require(rejected.Plan.Layers.Count == 0, "Topmost group was accepted for ordinary restacking.");
            Console.WriteLine("PASS: topmost-band change rejected rather than silently demoting or promoting windows.");
            var now = manager.Capture().First(w => w.Handle == windows[1].Handle);
            var stale = now with { VisualRect = now.VisualRect with { X = now.VisualRect.X + 40 } };
            var staleResult = manager.ApplyLayout(new([new(stale, now.VisualRect with { X = now.VisualRect.X + 80 })], [], []));
            Require(staleResult.Plan.Moves.Count == 0, "Stale geometry was applied.");
            Console.WriteLine("PASS: stale desktop geometry rejected.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            foreach (var child in children)
            {
                try { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } }
                finally { child.Dispose(); }
            }
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NeatWin.Reference;
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
            using var form = new SyntheticWindow { Text = "NeatWin synthetic smoke window", Size = new Size(500, 400),
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
                // Console-subsystem helper processes can create WinForms later; readiness is
                // established by the actual captured windows below, not WaitForInputIdle.
            }
            var manager = new WindowManager();
            var ids = children.Select(p => (uint)p.Id).ToHashSet();
            var watch = Stopwatch.StartNew();
            WindowSnapshot[] windows;
            do
            {
                Thread.Sleep(100);
                Require(children.All(p => !p.HasExited), "A synthetic child exited before creating its window.");
                windows = manager.Capture().Where(w => ids.Contains(w.ProcessId)).ToArray();
            } while (windows.Length != 3 && watch.ElapsedMilliseconds < 5000);
            Require(windows.Length == 3, "Could not capture all three synthetic windows.");
            if (args.Length == 1)
            {
                VerifyRecorder(args[0], windows[0], children);
                windows = manager.Capture().Where(w => ids.Contains(w.ProcessId)).ToArray();
            }
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
    private static void VerifyRecorder(string recorderPath, WindowSnapshot window, List<Process> children)
    {
        var started = DateTimeOffset.UtcNow;
        var recorder = Process.Start(new ProcessStartInfo(Path.GetFullPath(recorderPath)) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Recorder did not start.");
        children.Add(recorder);
        Require(recorder.WaitForInputIdle(15000), "Recorder has no message loop.");
        Thread.Sleep(600);
        Require(window.IsManageable, "Synthetic window is not manageable.");
        // Ask the synthetic child to enter the system move loop; system-only events are not forged.
        Require(PostMessage(window.Handle, 0x8005, nint.Zero, nint.Zero), "Synthetic lifecycle request failed.");
        // A modal system move loop need not dispatch WinForms timers. Drive its window-targeted
        // keyboard messages from the parent process rather than relying on a child timer.
        Thread.Sleep(300);
        for (var step = 0; step < 4; step++)
        {
            PostMessage(window.Handle, 0x0100, (nint)0x27, (nint)1);
            PostMessage(window.Handle, 0x0101, (nint)0x27, (nint)1);
            Thread.Sleep(150);
        }
        PostMessage(window.Handle, 0x0100, (nint)0x0D, (nint)1);
        PostMessage(window.Handle, 0x0101, (nint)0x0D, (nint)1);
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 8000)
        {
            Thread.Sleep(200);
            foreach (var path in Directory.Exists(IntentReferenceStore.DefaultDirectory)
                ? Directory.GetFiles(IntentReferenceStore.DefaultDirectory, "adjustments-*.jsonl") : [])
            {
                using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                while (reader.ReadLine() is string line)
                {
                    WindowAdjustmentObservation? observation;
                    try { observation = JsonSerializer.Deserialize<WindowAdjustmentObservation>(line); }
                    catch (JsonException) { continue; }
                    if (observation is null || observation.Time < started || observation.Version != 2) continue;
                    Require(observation.StartContext is not null && observation.EndContext is not null && observation.SettledContext is not null,
                        "Recorder omitted before, after or settled context.");
                    Require(observation.End.X != observation.Start.X, "Recorder lost the synthetic movement.");
                    Require(!line.Contains("Process") && !line.Contains("Handle") && !line.Contains("Title"), "Native identities or titles leaked into record.");
                    Console.WriteLine("PASS: independent recorder consumed synthetic move/size events and persisted v2 geometry/layer contexts without native IDs.");
                    recorder.Kill(); recorder.WaitForExit(5000);
                    return;
                }
            }
        }
        recorder.Refresh();
        if (!recorder.HasExited && recorder.MainWindowHandle != nint.Zero)
        {
            EnumChildWindows(recorder.MainWindowHandle, (child, _) =>
            {
                var text = new System.Text.StringBuilder(2048);
                GetWindowText(child, text, text.Capacity);
                if (text.Length > 0) Console.WriteLine("Recorder UI: " + text);
                return true;
            }, nint.Zero);
        }
        var actual = new WindowManager().Capture().FirstOrDefault(w => w.Handle == window.Handle);
        Console.WriteLine($"Synthetic window moved: {actual?.VisualRect.X != window.VisualRect.X}; recorder exited: {recorder.HasExited}");
        throw new InvalidOperationException("Recorder did not persist the synthetic gesture.");
    }

    private sealed class SyntheticWindow : Form
    {
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x8005)
            {
                Activate();
                PostMessage(Handle, 0x0112, (nint)0xF010, nint.Zero); // SC_MOVE; input is posted by the parent
                return;
            }
            base.WndProc(ref message);
        }
    }

    private delegate bool ChildCallback(nint child, nint data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, ChildCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, System.Text.StringBuilder text, int size);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}

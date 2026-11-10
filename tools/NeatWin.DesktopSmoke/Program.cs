using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NeatWin.Reference;
using NeatWin.Core;
using NeatWin.App;
using NeatWin.Recording;
using NeatWin.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (args.Length > 0 && args[0] == "--child")
        {
            var form = new SyntheticWindow { Title = "NeatWin synthetic smoke window", Width = 500, Height = 400,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = 80 + int.Parse(args[1]) * 90, Top = 80 };
            application.ShutdownMode = ShutdownMode.OnMainWindowClose;
            application.Run(form);
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
                // Console-subsystem helper processes can create WPF windows later; readiness is
                // established by the actual captured windows below, not WaitForInputIdle.
            }
            VerifyElevatedTargets(children);
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
            Require(SetWindowPos(first.Handle, (nint)(-2), 0, 0, 0, 0, 0x213), "Synthetic topmost cleanup failed.");
            VerifyAutomaticModes(manager, ids);
            VerifyModeSelector();
            VerifyIntegratedRecording(manager, manager.Capture().First(w => w.Handle == windows[0].Handle));
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
    private static void VerifyAutomaticModes(WindowManager manager, HashSet<uint> childIds)
    {
        IReadOnlyList<WindowSnapshot> CaptureChildren() => manager.Capture().Where(w => childIds.Contains(w.ProcessId)).ToArray();
        using var monitor = new WorkspaceAutoTidyMonitor(CaptureChildren, () => NativeWindowActivity.IsMoving);
        var requests = new List<AutomaticLayoutRequest>();
        monitor.Requested += requests.Add;
        var window = CaptureChildren()[0];
        Require(monitor.SetMode(AutomaticLayoutMode.FullAssist), "Full-assist setup failed.");
        Require(!monitor.WatchingWorkspace, "Full-assist installed broad workspace hooks.");
        Require(PostMessage(window.Handle, 0x8006, 0, 0), "External child move failed.");
        Pump(1000);
        Require(requests.Count == 0, "Full-assist reacted to an application move without a user gesture.");

        Require(monitor.SetMode(AutomaticLayoutMode.AutoFullAssist), "Automatic Smart hook setup failed.");
        Require(monitor.WatchingWorkspace, "Automatic mode did not install workspace hooks.");
        Require(PostMessage(window.Handle, 0x8006, 0, 0), "External child move failed.");
        Until(() => requests.Count == 1, 4000, "External geometry did not invoke automatic Smart.");
        Require(requests[0].Trigger == LayoutTrigger.WorkspaceChange, "Wrong external-change source.");
        requests.Clear(); Pump(1200);
        Require(requests.Count == 0, "Idle desktop repeatedly invoked Smart.");

        var before = CaptureChildren(); var current = before.First(w => w.Handle == window.Handle);
        var target = current.VisualRect with { X = current.VisualRect.X + 12 };
        var plan = new IntentLayoutPlan([new(current, target)], [], []);
        monitor.ApplicationRequested(before, plan);
        var applied = manager.ApplyLayout(plan);
        Require(applied.Plan.Moves.Count == 1, "Synthetic own action was not applied.");
        Pump(2400);
        Require(requests.Count == 0, "Our own native placement fed back into automatic Smart.");
        var actual = CaptureChildren(); var undo = LayoutUndoPlanner.Create(plan, actual);
        Require(undo.Moves.Count == 1, "Synthetic undo did not match.");
        monitor.ApplicationRequested(actual, undo); manager.ApplyLayout(undo); Pump(2400);
        Require(requests.Count == 0, "Undo triggered immediate reapplication.");

        Require(PostMessage(window.Handle, 0x8007, 0, 0), "Synthetic minimize failed.");
        Until(() => requests.Count == 1, 4000, "Minimize did not trigger a workspace update.");
        requests.Clear(); Pump(1100);
        Require(PostMessage(window.Handle, 0x8008, 0, 0), "Synthetic restore failed.");
        Until(() => requests.Count == 1, 4000, "Restore did not trigger a workspace update.");
        requests.Clear();

        Require(monitor.SetMode(AutomaticLayoutMode.Off), "Could not disable automatic layout.");
        Require(!monitor.WatchingWorkspace, "Disabled mode retained workspace hooks.");
        Require(PostMessage(window.Handle, 0x8006, 0, 0), "Disabled-mode child move failed.");
        Pump(1000); Require(requests.Count == 0, "Disabled mode still requested layout.");
        Console.WriteLine("PASS: real external move/minimize/restore invoke full auto; own placement and undo do not loop; mode changes disable hooks.");
    }

    private static void VerifyModeSelector()
    {
        var window = new MainWindow(HotkeyBinding.Default, new(), new(), AutomaticLayoutMode.Off);
        try
        {
            window.Show(); Pump(100);
            var choices = Descendants(window).OfType<RadioButton>().Where(b => b.GroupName == "Automation").ToArray();
            Require(choices.Length == 4 && choices.All(b => b.IsVisible && b.ActualWidth >= 75), "Four automation choices must remain directly visible.");
            var changes = 0; window.AutoTidyChangeRequested += (_, _) => changes++;
            foreach (var choice in choices.OrderBy(b => int.Parse((string)b.Tag))) choice.IsChecked = true;
            Require(changes == 3, "Automation selector emitted the wrong transitions.");
            window.ShowRecorderPage(); window.SetRecorderStatus(new RecorderStatus(false, true, 7, 11, 1, null)); Pump(80);
            Require(((Button)window.FindName("RecorderPauseButton")).IsVisible, "Recorder pause is missing.");
            Require(((Button)window.FindName("RecorderExportButton")).IsVisible, "Recorder export is missing.");
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create("automation-mode-ui.png"); png.Save(file);
            Console.WriteLine("PASS: real WPF view exposes four modes and the integrated recorder controls.");
        }
        finally { window.AllowCloseAndClose(); }
    }

    private static void VerifyIntegratedRecording(WindowManager manager, WindowSnapshot targetWindow)
    {
        var dir = Path.Combine(Path.GetTempPath(), "NeatWin-integrated-smoke-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var store = new IntentReferenceStore(dir);
            using var recorder = new RecorderSession(store);
            Require(recorder.Status.IsAvailable, "Integrated recorder hooks unavailable.");
            using var journal = new LayoutRunJournal(manager, store, recorder);
            var completions = new List<LayoutRunObservation>();
            journal.Completed += completions.Add;
            var before = manager.Capture();
            var w = before.Single(w => w.Handle == targetWindow.Handle);
            var plan = new IntentLayoutPlan([new(w, w.VisualRect with { X = w.VisualRect.X + 9 })], [], []);
            manager.ApplyLayout(plan);
            journal.Record(before, plan, "integrated-test", []);
            Until(() => completions.Count == 1, 4000, "Integrated journal did not verify placement.");
            Pump(350);
            var records = Directory.EnumerateFiles(dir, "plans-*.jsonl").SelectMany(File.ReadLines)
                .Select(line => JsonSerializer.Deserialize<LayoutRunObservation>(line)!).ToArray();
            Require(records.Length == 2 && records.All(r => r.Session == recorder.SessionId), "Journal/session ids are not shared.");
            Require(records[0].Targets.Single().Id == recorder.Identity.Id(w), "Window ids are not shared.");
            recorder.TogglePause();
            string Fingerprint() => string.Join(";", Directory.EnumerateFiles(dir).Order().Select(f => File.ReadAllText(f)));
            var paused = Fingerprint();
            journal.Record(manager.Capture(), new([], [], []), "paused-test", []);
            Until(() => completions.Count == 2, 4000, "Pause stopped runtime verification.");
            Require(Fingerprint() == paused, "Pause still wrote diagnostics.");
            recorder.TogglePause();
            journal.Record(manager.Capture(), new([], [], []), "clear-test", []);
            recorder.Clear(); recorder.TogglePause();
            Until(() => completions.Count == 3, 4000, "Clear stopped runtime verification.");
            Require(!Directory.EnumerateFiles(dir, "*.jsonl").Any(), "An old completion resurrected cleared records.");
            Console.WriteLine("PASS: integrated recorder shares anonymous session/ids with plans; pause/clear stops logging, not verification.");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static void VerifyElevatedTargets(IEnumerable<Process> children)
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        Require(new System.Security.Principal.WindowsPrincipal(identity).IsInRole(
            System.Security.Principal.WindowsBuiltInRole.Administrator), "Native checks require an elevated runner.");
        foreach (var child in children)
        {
            Require(OpenProcessToken(child.Handle, 8, out var token), "Could not read synthetic process token.");
            try { Require(GetTokenInformation(token, 20, out var elevated, sizeof(int), out _) && elevated != 0,
                "Synthetic target is not elevated."); }
            finally { CloseHandle(token); }
        }
        Console.WriteLine("PASS: native harness and all synthetic window targets have administrator tokens.");
    }
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint token, int kind, out int value, int length, out int returned);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Until(Func<bool> ready, int limit, string error)
    {
        var watch = Stopwatch.StartNew();
        while (!ready() && watch.ElapsedMilliseconds < limit) Pump(30);
        Require(ready(), error);
    }

    private sealed class SyntheticWindow : Window
    {
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!.AddHook(OnMessage);
        }
        private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        {
            switch (message)
            {
                case 0x8006: Left += 25; break;
                case 0x8007: WindowState = WindowState.Minimized; break;
                case 0x8008: WindowState = WindowState.Normal; break;
                case 0x8005: Activate(); PostMessage(hwnd, 0x0112, (nint)0xF010, 0); break;
                default: return 0;
            }
            handled = true; return 0;
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

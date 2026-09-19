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
            if (args.Length == 1)
                VerifyRecorder(args[0], manager.Capture().First(w => w.Handle == windows[0].Handle), children);
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
        using var form = new MainWindow(HotkeyBinding.Default, new(), new(), AutomaticLayoutMode.Off);
        form.Show(); Pump(100);
        Require(!Descendants(form).OfType<ComboBox>().Any(c => c.AccessibleName == "自动整理档位"), "Automation is still a dropdown.");
        var selector = Descendants(form).OfType<SegmentedSelector<AutomaticLayoutMode>>().Single();
        var choices = Descendants(selector).OfType<Button>().ToArray();
        Require(choices.Length == 4, "Mode selector does not expose exactly four choices.");
        var changes = 0;
        form.AutoTidyChangeRequested += (_, _) => changes++;
        foreach (var mode in Enum.GetValues<AutomaticLayoutMode>())
            choices.Single(b => b.Text == AutomaticLayoutPolicy.Name(mode)).PerformClick();
        Require(changes == 3, "Mode selection did not produce exactly the expected changes.");
        var settingsTab = Descendants(form).OfType<Button>().Single(b => b.Text == "整理设置");
        foreach (var button in choices)
            Require(button.Visible && button.PointToScreen(Point.Empty).Y < settingsTab.PointToScreen(Point.Empty).Y &&
                button.Width >= TextRenderer.MeasureText(button.Text, button.Font).Width,
                "Four choices must be visible, readable and above the tabs.");
        Require(!Descendants(form).OfType<Button>().Any(b => b.Text == "完整平铺"), "Unexpected fifth tiling action.");
        var recorderTab = Descendants(form).OfType<Button>().Single(b => b.Text == "记录器");
        recorderTab.PerformClick(); Pump(80);
        form.SetRecorderStatus(new RecorderStatus(false, true, 7, 11, 1, null));
        Require(Descendants(form).OfType<Button>().Any(b => b.Visible && b.Text == "暂停记录"), "Recorder pause is missing.");
        Require(Descendants(form).OfType<Button>().Any(b => b.Visible && b.Text == "导出记录"), "Recorder export is missing.");
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save("automation-mode-ui.png", System.Drawing.Imaging.ImageFormat.Png);
        form.AllowCloseAndClose();
        Console.WriteLine("PASS: four directly clickable modes across the top; no tiling action; integrated recorder tab; screenshot retained.");
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

    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void Pump(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(15); }
    }
    private static void Until(Func<bool> ready, int limit, string error)
    {
        var watch = Stopwatch.StartNew();
        while (!ready() && watch.ElapsedMilliseconds < limit) Pump(30);
        Require(ready(), error);
    }

    private static void VerifyRecorder(string recorderPath, WindowSnapshot window, List<Process> children)
    {
        var started = DateTimeOffset.UtcNow;
        var startInfo = new ProcessStartInfo(Path.GetFullPath(recorderPath)) { UseShellExecute = false, RedirectStandardError = true };
        startInfo.Environment["NEATWIN_EVENT_DIAGNOSTICS"] = "1";
        var recorder = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Recorder did not start.");
        children.Add(recorder);
        recorder.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine("Recorder event: " + e.Data); };
        recorder.BeginErrorReadLine();
        Require(recorder.WaitForInputIdle(15000), "Recorder has no message loop.");
        // Input-idle can be reached by a startup/helper message loop before the constructor
        // installs its hooks. The recorder shows its normal panel only after initialization.
        var readiness = Stopwatch.StartNew();
        do
        {
            Thread.Sleep(100);
            recorder.Refresh();
            Require(!recorder.HasExited, "Recorder exited during initialization.");
        } while ((recorder.MainWindowHandle == nint.Zero || !recorder.MainWindowTitle.Contains("习惯记录器 v2")) && readiness.ElapsedMilliseconds < 15000);
        Require(recorder.MainWindowHandle != nint.Zero && recorder.MainWindowTitle.Contains("习惯记录器 v2"), "Recorder did not show its initialized panel.");
        Thread.Sleep(150);
        Require(window.IsManageable, "Synthetic window is not manageable.");
        // Ask the synthetic child to enter the system move loop; system-only events are not forged.
        Require(PostMessage(window.Handle, 0x8005, nint.Zero, nint.Zero), "Synthetic lifecycle request failed.");
        // Posted WM_KEYDOWN messages are not equivalent to the real keyboard input stream.
        // This test injects only into its own foreground child on the isolated x64 CI desktop.
        Require(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" && IntPtr.Size == 8,
            "Input smoke test is restricted to the isolated x64 Actions desktop.");
        Thread.Sleep(350);
        for (var step = 0; step < 4; step++)
        {
            PressKey(window.Handle, 0x27);
            Thread.Sleep(180);
        }
        PressKey(window.Handle, 0x0D);
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

    private static void PressKey(nint ownedWindow, ushort key)
    {
        Require(GetForegroundWindow() == ownedWindow, "Synthetic input target lost foreground focus; no keys sent.");
        var inputs = new[] { new Input { Type = 1, VirtualKey = key }, new Input { Type = 1, VirtualKey = key, Flags = 2 } };
        Require(SendInput(2, inputs, Marshal.SizeOf<Input>()) == 2, "Synthetic keyboard input was not accepted.");
    }
    [StructLayout(LayoutKind.Explicit, Size = 40)] // Win64 INPUT; the union begins at byte 8.
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort VirtualKey;
        [FieldOffset(12)] public uint Flags;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    private sealed class SyntheticWindow : Form
    {
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x8006) { Left += 25; return; }
            if (message.Msg == 0x8007) { WindowState = FormWindowState.Minimized; return; }
            if (message.Msg == 0x8008) { WindowState = FormWindowState.Normal; return; }
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

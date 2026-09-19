using System.Diagnostics;
using System.Text.Json;
using NeatWin.Core;

// Deterministic synthetic geometry only. No recorder files, native windows or user data.
var count = args.Length > 0 ? int.Parse(args[0]) : 1000;
if (count is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(count));
const int seed = 9192601, steps = 12;
var random = new Random(seed);
var watch = Stopwatch.StartNew();
var calls = 0; var changed = 0; var settled = 0; var cycles = 0; var limited = 0;
var undoChecks = 0; var reuseChecks = 0; var scored = 0; var sample = -1;
try
{
    for (sample = 0; sample < count; sample++)
    {
        var (width, height) = new[] { (2560, 1380), (5120, 1390), (1920, 1040), (1080, 1840), (800, 550) }[sample % 5];
        var area = new RectI(sample % 3 == 0 ? -width : 0, sample % 7 == 0 ? -200 : 0, width, height);
        var other = new RectI(area.Right, area.Top, 1920, 1040);
        TaskDisplay[] displays = [new((nint)1, area, area), new((nint)2, other, other)];
        var dpi = new uint[] { 96, 120, 144, 168, 192 }[(sample / 5) % 5];
        var windows = Enumerable.Range(1, random.Next(2, 9)).Select(id =>
        {
            var second = sample % 4 == 0 && id % 2 == 0;
            var work = second ? other : area;
            var rw = random.Next(180, work.Width + 180); var rh = random.Next(140, work.Height + 100);
            var rect = new RectI(work.X + random.Next(-100, Math.Max(1, work.Width - rw / 2)),
                work.Y + random.Next(-80, Math.Max(1, work.Height / 2)), rw, rh);
            return new WindowSnapshot((nint)id, rect, rect, work, (nint)(second ? 2 : 1), default,
                id % 3 != 0, id == 1, true, id, second ? 144u : dpi, (uint)id,
                id == 1 && sample % 11 == 0);
        }).ToArray();
        var context = new TaskLayoutContext(Profile: new(AllowUsefulResize: sample % 9 != 0,
            AllowPeripheralBleed: sample % 3 != 0), Displays: displays,
            Calibration: sample % 2 == 0 ? new(area, dpi, 600, 700) : null,
            WindowHints: sample % 5 == 0 ? [new((nint)2, PassiveVisual: true, ProtectPeriphery: sample % 10 == 0)] : null,
            PreferReversibleVerticalFill: sample % 2 == 0);
        var options = new TidyOptions { SmartStrength = (SmartTidyStrength)(sample % 3) };
        var seen = new HashSet<string> { Key(windows) }; var completed = false;
        for (var step = 0; step < steps; step++)
        {
            calls++;
            var plan = IntentLayoutPlanner.CreateDetailedPlan(Visible(windows), options, desktop: windows, taskContext: context);
            foreach (var candidate in plan.Groups.SelectMany(g => g.Candidates).Where(c => c.Rejection is null))
            {
                Require(candidate.Cost is double value && double.IsFinite(value), "non-finite cost");
                Require(candidate.Breakdown is not null && Math.Abs(candidate.Breakdown.Total - candidate.Cost!.Value) < 1e-9,
                    "cost does not match diagnostic terms");
                scored++;
            }
            Require(plan.Moves.Select(m => m.Window.Handle).Distinct().Count() == plan.Moves.Count, "duplicate moves");
            foreach (var move in plan.Moves)
            {
                var before = move.Window; var target = move.TargetVisualRect; var work = before.WorkArea;
                Require(IntentEvidence.IsValidRect(target), "invalid rectangle");
                Require(!before.IsTopmost, "moved a pinned window");
                if (!before.IsResizable || !context.Preferences.AllowUsefulResize)
                    Require(target.Width == before.VisualRect.Width && target.Height == before.VisualRect.Height, "resized despite opt-out");
                var rescue = plan.Groups.Any(g => g.Selected == "rescue" && g.Handles.Contains(before.Handle)) &&
                    (before.VisualRect.Width > work.Width || before.VisualRect.Height > work.Height);
                if (!rescue)
                {
                    Require(target.Top >= work.Top && target.Bottom <= work.Bottom, "vertical overflow");
                    var budget = (int)Math.Round(Math.Min(context.Preferences.MaximumBleedDip * before.Dpi / 96.0,
                        before.VisualRect.Width * .035));
                    if (!context.Preferences.AllowPeripheralBleed || context.WindowHints?.Any(h => h.Handle == before.Handle && h.ProtectPeriphery) == true) budget = 0;
                    Require(target.Left >= work.Left - budget && target.Right <= work.Right + budget, "bleed budget");
                    if (before.MonitorHandle == (nint)1) Require(target.Right <= work.Right, "bleed entered right monitor");
                    else Require(target.Left >= work.Left, "bleed entered left monitor");
                }
            }
            foreach (var layer in plan.Layers) Require(WindowLayerSafety.CanReorder(layer.FrontToBack, windows), "unsafe layer block");
            if (plan.Moves.Count == 0 && plan.Layers.Count == 0) { settled++; completed = true; break; }
            changed++;
            var after = Project(windows, plan);
            var undo = LayoutUndoPlanner.Create(plan, after);
            Require(Key(Project(after, undo)) == Key(windows), "exact planned layout did not undo");
            undoChecks++;
            var now = DateTimeOffset.UtcNow;
            var session = new TaskLayoutSession(); session.Requested(windows, plan, now); session.Verify(after);
            var repeat = session.Capture(after, context.Preferences, displays, context.Calibration, now);
            Require(repeat.VerifiedRepeat, "verified exact result not reusable");
            var repeated = IntentLayoutPlanner.CreateDetailedPlan(Visible(after), options, desktop: after, taskContext: repeat);
            Require(repeated.Moves.Count == 0 && repeated.Layers.Count == 0, "repeat reused result changed geometry");
            reuseChecks++;
            windows = after;
            if (!seen.Add(Key(windows))) { cycles++; completed = true; break; }
        }
        if (!completed) limited++;
    }
    Console.WriteLine(JsonSerializer.Serialize(new { seed, scenes = count, maximumStatelessPasses = steps,
        plannerCalls = calls, changedPasses = changed, finiteCandidateChecks = scored, undoRoundTrips = undoChecks,
        verifiedRepeatChecks = reuseChecks, statelessSettled = settled, statelessCycles = cycles,
        statelessBudgetExhausted = limited, seconds = watch.Elapsed.TotalSeconds,
        caveat = "Synthetic contract tests; not real-desktop satisfaction, real mixed-DPI hardware, or intent accuracy." },
        new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception e) { Console.Error.WriteLine($"seed={seed}, scene={sample}: {e}"); return 1; }

static VisibleWindow[] Visible(WindowSnapshot[] windows) => windows.OrderBy(w => w.ZOrder)
    .Select(w => new VisibleWindow(w, w.VisualRect.Area, w.VisualRect.Area, 1)).ToArray();
static string Key(IEnumerable<WindowSnapshot> windows) => string.Join(";", windows.OrderBy(w => w.Handle)
    .Select(w => $"{w.Handle}:{w.VisualRect}:{w.ZOrder}"));
static WindowSnapshot[] Project(WindowSnapshot[] windows, IntentLayoutPlan plan)
{
    var targets = plan.Moves.ToDictionary(m => m.Window.Handle, m => m.TargetVisualRect);
    var ranks = windows.ToDictionary(w => w.Handle, w => w.ZOrder);
    foreach (var layer in plan.Layers)
    {
        var slots = layer.FrontToBack.Select(w => ranks[w.Handle]).Order().ToArray();
        for (var i = 0; i < slots.Length; i++) ranks[layer.FrontToBack[i].Handle] = slots[i];
    }
    return windows.Select(w => w with { VisualRect = targets.GetValueOrDefault(w.Handle, w.VisualRect),
        OuterRect = targets.GetValueOrDefault(w.Handle, w.OuterRect), ZOrder = ranks[w.Handle] }).ToArray();
}
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

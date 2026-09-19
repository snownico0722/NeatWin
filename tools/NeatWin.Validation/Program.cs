using System.Text.Json;
using NeatWin.Core;

// No model changes, fitting, recorder upload, or satisfaction labels. The caller supplies
// hypothetical missing context explicitly. Private inputs/outputs are read only on that host.
if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: NeatWin.Validation input.jsonl output.jsonl [reference.json]");
    return 2;
}
var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var reference = args.Length == 3 ? JsonSerializer.Deserialize<IntentReferenceDocument>(File.ReadAllText(args[2]), json) : null;
var processed = 0; var failures = 0;
using var output = new StreamWriter(args[1]);
foreach (var line in File.ReadLines(args[0]))
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    Input? input = null;
    try
    {
        input = JsonSerializer.Deserialize<Input>(line, json) ?? throw new InvalidDataException("Missing input");
        if (input.Rects.Length is < 1 or > 8 || input.MaxPasses is < 1 or > 32 || input.Dpi is < 48 or > 384 ||
            !IntentEvidence.IsValidRect(input.Area) || input.Rects.Any(r => !IntentEvidence.IsValidRect(r)))
            throw new InvalidDataException("Invalid bounded scene");
        var n = input.Rects.Length;
        var initialOrder = input.Order ?? Enumerable.Range(0, n).ToArray();
        if (initialOrder.Length != n || initialOrder.Order().SequenceEqual(Enumerable.Range(0, n)) == false)
            throw new InvalidDataException("Order must be a permutation");
        var windows = input.Rects.Select((r, i) => new WindowSnapshot((nint)(i + 1), r, r,
            input.Area, (nint)1, default, input.Resizable?[i] ?? input.AllowResize, i == input.Foreground,
            true, Array.IndexOf(initialOrder, i), input.Dpi, (uint)(i + 1), input.Topmost?[i] ?? false))
            .OrderBy(w => w.ZOrder).ToArray();
        if (!Enum.TryParse<TaskRelation>(input.Relation, out var relation)) throw new InvalidDataException("Unknown relation");
        TaskPairHint[]? pairs = relation == TaskRelation.Automatic ? null :
            Enumerable.Range(0, n).SelectMany(i => Enumerable.Range(i + 1, n - i - 1)
                .Select(j => new TaskPairHint((nint)(i + 1), (nint)(j + 1), relation))).ToArray();
        var hints = Enumerable.Range(0, n).Where(i => input.Passive == i || input.Protect || input.DemandWidth is not null)
            .Select(i => new TaskWindowHint((nint)(i + 1), input.Passive == i, input.Protect,
                input.DemandWidth, input.DemandHeight)).ToArray();
        TaskDisplay[]? displays = input.Topology switch
        {
            "single" => [new((nint)1, input.Area, input.Area)],
            "right-neighbor" => [new((nint)1, input.Area, input.Area),
                new((nint)2, new(input.Area.Right, input.Area.Top, input.Area.Width, input.Area.Height),
                    new(input.Area.Right, input.Area.Top, input.Area.Width, input.Area.Height))],
            _ => null,
        };
        var context = new TaskLayoutContext(Profile: new(AllowUsefulResize: input.AllowResize,
                AllowPeripheralBleed: input.Bleed, JointVisibilityWeight: input.JointWeight,
                SpatialContinuityWeight: input.ContinuityWeight), Displays: displays,
            Calibration: input.PhysicalWidth > 0 && input.ViewingDistance > 0 ?
                new(input.Area, input.Dpi, input.PhysicalWidth, input.ViewingDistance) : null,
            PairHints: pairs, WindowHints: hints, PreferReversibleVerticalFill: input.VerticalFill);
        var options = new TidyOptions { SmartStrength = (SmartTidyStrength)input.Strength };
        var analyzer = new VisibilityAnalyzer();
        var states = new List<object>(); var seen = new Dictionary<string, int> { [Key(windows)] = 0 };
        var status = "budget"; var cycleStart = -1; var undoOk = true; var repeatOk = true;
        object? firstTrace = null;
        for (var pass = 0; pass < input.MaxPasses; pass++)
        {
            var visible = input.VisibleFilter ? analyzer.SelectVisibleWorkingSet(windows, includeStackAccess: true) :
                windows.Select(w => new VisibleWindow(w, w.VisualRect.Area, w.VisualRect.Area, 1)).ToArray();
            var plan = IntentLayoutPlanner.CreateDetailedPlan(visible, options,
                input.UseReference ? reference : null, windows, context);
            if (pass == 0) firstTrace = plan.Groups.Select(g => new
            {
                Ids = g.Handles.Select(h => (int)h - 1).ToArray(), g.Selected, g.StackEvidence,
                g.Candidates, g.Task,
            }).ToArray();
            var after = Project(windows, plan);
            states.Add(new
            {
                Pass = pass, Eligible = visible.Select(v => (int)v.Window.Handle - 1).ToArray(),
                Moved = plan.Moves.Count, Layers = plan.Layers.Count,
                Families = plan.Groups.Select(g => g.Selected).ToArray(),
                Rects = after.OrderBy(w => w.Handle).Select(w => w.VisualRect).ToArray(),
                Order = after.OrderBy(w => w.ZOrder).Select(w => (int)w.Handle - 1).ToArray(),
            });
            if (plan.Moves.Count == 0 && plan.Layers.Count == 0) { status = "settled"; break; }
            var undo = LayoutUndoPlanner.Create(plan, after);
            undoOk &= Key(Project(after, undo)) == Key(windows);
            var now = DateTimeOffset.UtcNow; var session = new TaskLayoutSession();
            session.Requested(windows, plan, now); session.Verify(after);
            repeatOk &= session.Capture(after, context.Preferences, displays ?? [], context.Calibration, now).VerifiedRepeat;
            windows = after;
            if (seen.TryGetValue(Key(windows), out cycleStart)) { status = "cycle"; break; }
            seen[Key(windows)] = pass + 1;
        }
        output.WriteLine(JsonSerializer.Serialize(new { input.Id, Status = status, CycleStart = cycleStart,
            UndoOk = undoOk, RepeatOk = repeatOk, States = states, FirstTrace = firstTrace }));
    }
    catch (Exception error)
    {
        failures++;
        output.WriteLine(JsonSerializer.Serialize(new { Id = input?.Id, Error = error.Message }));
    }
    processed++;
}
Console.WriteLine(JsonSerializer.Serialize(new { processed, failures, sourceModel = "compiled-production-core",
    caveat = "Geometric scenario audit, not human intent accuracy or satisfaction. No parameter fitting." }));
return failures == 0 ? 0 : 1;

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
        OuterRect = targets.GetValueOrDefault(w.Handle, w.OuterRect), ZOrder = ranks[w.Handle] }).OrderBy(w => w.ZOrder).ToArray();
}
sealed record Input(string Id, RectI[] Rects, RectI Area, uint Dpi = 120, int[]? Order = null,
    int Foreground = 0, bool[]? Resizable = null, bool[]? Topmost = null, bool AllowResize = true,
    bool VisibleFilter = true, string Relation = "Automatic", string Topology = "unknown",
    bool Bleed = true, bool Protect = false, int Passive = -1, double? DemandWidth = null,
    double? DemandHeight = null, bool VerticalFill = false, double PhysicalWidth = 0,
    double ViewingDistance = 0, int Strength = 1, int MaxPasses = 12, bool UseReference = false,
    double JointWeight = 1, double ContinuityWeight = 1);

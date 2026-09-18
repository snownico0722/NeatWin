using System.Text.Json;
using NeatWin.Core;

if (args.Length != 2) { Console.Error.WriteLine("Usage: NeatWin.Replay <observations.jsonl> <results.jsonl>"); return 2; }
try
{
    using var output = new StreamWriter(args[1]);
    var lineNumber = 0;
    foreach (var line in File.ReadLines(args[0]))
    {
        lineNumber++;
        if (string.IsNullOrWhiteSpace(line)) continue;
        var o = JsonSerializer.Deserialize<WindowAdjustmentObservation>(line);
        if (o is null || !IntentEvidence.IsValidRect(o.WorkArea) || !IntentEvidence.IsValidRect(o.End)) continue;
        // Old logs lack layer order and resizability. Test both orders as hypothetical scenes,
        // not reconstructions of a user's exact desktop or labels of their true intent.
        var rects = new[] { o.End }.Concat(o.Neighbors.Take(7)).ToArray();
        foreach (var reversed in new[] { false, true })
        {
            var windows = rects.Select((r, i) => new WindowSnapshot((nint)(i + 1), r, r, o.WorkArea,
                (nint)1, default, true, i == 0, true, reversed ? rects.Length - i : i, o.Dpi)).OrderBy(w => w.ZOrder).ToArray();
            var visible = windows.Select(w => new VisibleWindow(w, w.VisualRect.Area, w.VisualRect.Area, 1)).ToArray();
            var plan = IntentLayoutPlanner.CreateDetailedPlan(visible, new TidyOptions(), desktop: windows);
            var target = windows.Select(w => plan.Moves.FirstOrDefault(m => m.Window.Handle == w.Handle)?.TargetVisualRect ?? w.VisualRect).ToArray();
            var moved = windows.Select((w, i) => Math.Sqrt(Math.Pow(w.VisualRect.X - target[i].X, 2) + Math.Pow(w.VisualRect.Y - target[i].Y, 2))).ToArray();
            var layerOrder = windows.ToDictionary(w => w.Handle, w => w.ZOrder);
            foreach (var layer in plan.Layers)
            {
                var ranks = layer.FrontToBack.Select(w => layerOrder[w.Handle]).Order().ToArray();
                for (var i = 0; i < ranks.Length; i++) layerOrder[layer.FrontToBack[i].Handle] = ranks[i];
            }
            var placed = windows.Select((w, i) => w with { VisualRect = target[i], OuterRect = target[i], ZOrder = layerOrder.GetValueOrDefault(w.Handle, w.ZOrder) }).OrderBy(w => w.ZOrder).ToArray();
            var second = IntentLayoutPlanner.CreateDetailedPlan(placed.Select(w => new VisibleWindow(w, w.VisualRect.Area, w.VisualRect.Area, 1)).ToArray(), new TidyOptions(), desktop: placed);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Line = lineNumber, Reversed = reversed, Assumptions = "unknown-order-and-resizability; all-neighbors-included",
                Families = plan.Groups.Select(g => g.Selected), Moved = plan.Moves.Count, Layers = plan.Layers.Count,
                MaxTranslation = moved.Max(), SizesPreserved = windows.Select((w, i) => w.VisualRect.Width == target[i].Width && w.VisualRect.Height == target[i].Height).All(x => x),
                SecondPassMoves = second.Moves.Count, SecondPassLayers = second.Layers.Count,
                Targets = target, Candidates = plan.Groups.SelectMany(g => g.Candidates),
            }));
        }
    }
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

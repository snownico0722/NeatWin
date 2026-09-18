namespace NeatWin.Core;

/// <summary>
/// Compare task-conditioned local layouts. A manual endpoint is evidence, not the optimum.
/// Follow-hand assistance remains a separate small correction.
/// </summary>
internal static partial class IntentLayoutPlanner
{
    internal static IReadOnlyList<TidyMove> CreatePlan(IReadOnlyList<VisibleWindow> visible,
        TidyOptions options, IntentReferenceDocument? reference = null) =>
        CreateDetailedPlan(visible, options, reference).Moves;

    internal static IntentLayoutPlan CreateDetailedPlan(IReadOnlyList<VisibleWindow> visible,
        TidyOptions options, IntentReferenceDocument? reference = null,
        IReadOnlyList<WindowSnapshot>? desktop = null, TaskLayoutContext? taskContext = null)
    {
        if (options.AlgorithmMode == TidyAlgorithmMode.Classic)
            return new(new TidyEngine().CreatePlan(visible, options), [], []);
        taskContext ??= new TaskLayoutContext();
        if (taskContext.VerifiedRepeat) return new([], [], []);
        var moves = new List<TidyMove>();
        var layers = new List<WindowLayerOrder>();
        var traces = new List<LayoutGroupTrace>();
        foreach (var monitor in visible.Where(v => v.Window.IsManageable && !v.Window.VisualRect.IsEmpty)
            .GroupBy(v => v.Window.MonitorHandle))
        {
            var all = monitor.OrderBy(v => v.Window.ZOrder).ThenBy(v => v.Window.Handle).ToArray();
            var area = all[0].Window.WorkArea;
            if (area.IsEmpty) continue;
            var settled = all.ToDictionary(v => v.Window.Handle, v => v.Window.VisualRect);
            var reach = Math.Clamp(Math.Min(area.Width, area.Height) * 0.18, 100, 260);
            foreach (var group in Groups(all, reach))
            {
                var hint = IntentEvidence.Resolve(reference, area, group[0].Window.Dpi, DateTimeOffset.UtcNow);
                var original = group.Select(v => v.Window.VisualRect).ToArray();
                var task = InferTask(group, original, taskContext);
                var order = Enumerable.Range(0, group.Length).ToArray();
                var blockers = desktop?.Where(w => !all.Any(v => v.Window.Handle == w.Handle) &&
                    w.MonitorHandle == group[0].Window.MonitorHandle && w.ZOrder < group.Max(v => v.Window.ZOrder)).ToArray() ?? [];
                var obstacles = desktop?.Where(w => !group.Any(v => v.Window.Handle == w.Handle) &&
                    w.MonitorHandle == group[0].Window.MonitorHandle).ToArray() ?? [];
                var generationNotes = new List<LayoutCandidateTrace>();
                var candidates = new List<Candidate> { new("keep", original, order) };
                var rescue = group.Select(v => Fit(v.Window.VisualRect, v.Window, options)).ToArray();
                if (!rescue.SequenceEqual(original)) candidates.Add(new("rescue", rescue, order));
                var local = new TidyEngine().CreatePlan(group, options).ToDictionary(m => m.Window.Handle, m => m.TargetVisualRect);
                candidates.Add(new("local", group.Select((v, i) => local.GetValueOrDefault(v.Window.Handle, original[i])).ToArray(), order));
                if (group.Length is >= 2 and <= 12)
                {
                    AddPackedCandidate("columns", group.Length, false);
                    AddPackedCandidate("columns", group.Length, true);
                    AddPackedCandidate("rows", 1, false);
                    for (var columns = 2; columns < group.Length; columns++)
                        AddPackedCandidate("grid", columns, false);
                    AddOverlapping(candidates, group, original, area, hint);
                    AddOrders(candidates, "restack", original, group);
                    AddTaskCandidates(candidates, group, original, area, task, taskContext);
                }
                var stackSignal = StackSignal(group, original);
                TaskCostBreakdown Breakdown(Candidate c) => TaskCost(group, original, c, task, area, taskContext, hint, obstacles);
                double Cost(Candidate c) => Breakdown(c).Total;
                candidates = candidates.DistinctBy(c => string.Join(";", c.Rects) + ":" + string.Join(",", c.Order)).ToList();
                var best = candidates[0]; var baseline = Cost(best); var bestScore = baseline;
                var audits = new List<LayoutCandidateTrace>(generationNotes) { new("keep", baseline, null, Breakdown(best)) };
                foreach (var candidate in candidates.Skip(1))
                {
                    var rejection = !candidate.Order.SequenceEqual(order) &&
                        !WindowLayerSafety.CanReorder(group.Select(v => v.Window).ToArray(), desktop ?? all.Select(v => v.Window).ToArray())
                        ? "layer-band-or-interleaved-window" : !TaskSafe(group, original, candidate, area, options, taskContext, obstacles) ? "geometry-budget" :
                        HitsOutsideGroup(settled, group, original, candidate.Rects) ? "other-group" :
                        blockers.Any(w => candidate.Rects.Select((r, i) => r.Intersect(w.VisualRect).Area > original[i].Intersect(w.VisualRect).Area).Any(b => b)) ? "fixed-occluder" :
                        null;
                    if (rejection is not null) { audits.Add(new(candidate.Kind, null, rejection)); continue; }
                    var score = Cost(candidate);
                    audits.Add(new(candidate.Kind, score, null, Breakdown(candidate)));
                    var threshold = options.SmartStrength switch
                    {
                        SmartTidyStrength.Gentle => 0.18,
                        SmartTidyStrength.Assertive => 0.025,
                        _ => 0.065,
                    };
                    var needsRescue = options.RescueOffscreenWindows && original.Any(r =>
                        r.Intersect(area).Area < r.Area * .90 || r.Top < area.Top || r.Bottom > area.Bottom);
                    if ((score < bestScore && score < baseline - threshold) || (best.Kind == "keep" && needsRescue && candidate.Kind == "rescue"))
                    { best = candidate; bestScore = score; }
                }
                for (var i = 0; i < group.Length; i++)
                {
                    settled[group[i].Window.Handle] = best.Rects[i];
                    if (best.Rects[i] != group[i].Window.VisualRect) moves.Add(new(group[i].Window, best.Rects[i]));
                }
                if (!best.Order.SequenceEqual(order)) layers.Add(new(best.Order.Select(i => group[i].Window).ToArray()));
                traces.Add(new(group.Select(v => v.Window.Handle).ToArray(), best.Kind, stackSignal, audits.ToArray(),
                    new(task, taskContext.Calibration?.Matches(area, group[0].Window.Dpi) == true ? "calibrated-flat-screen-angle" : "normalized-distance-not-gaze",
                        ["hypothesis-weights-not-calibrated-probabilities", "content-prior-not-semantic-recognition", "stable-is-not-satisfaction"])));

                void AddPackedCandidate(string kind, int columns, bool equalize)
                {
                    var packed = new List<RectI[]>();
                    AddPacked(packed, group, original, area, hint.GapPixels, columns, equalize);
                    if (packed.Count == 0) generationNotes.Add(new(kind, null, "no-size-safe-packing"));
                    foreach (var rects in packed) candidates.Add(new(kind, rects, order));
                }
            }
        }
        return new(moves, layers, traces);
    }

    private static IEnumerable<VisibleWindow[]> Groups(VisibleWindow[] windows, double reach)
    {
        var remaining = new HashSet<int>(Enumerable.Range(0, windows.Length));
        while (remaining.Count != 0)
        {
            var indices = new List<int> { remaining.Min() };
            remaining.Remove(indices[0]);
            for (var i = 0; i < indices.Count; i++)
                foreach (var j in remaining.ToArray())
                    if (Related(windows[indices[i]].Window.VisualRect, windows[j].Window.VisualRect, reach))
                    {
                        indices.Add(j);
                        remaining.Remove(j);
                    }
            yield return indices.Order().Select(i => windows[i]).ToArray();
        }
    }

    private static bool Related(RectI a, RectI b, double reach) =>
        a.Intersect(b).Area > 0 ||
        (a.VerticalOverlapRatio(b) >= 0.3 && RectI.IntervalGap(a.Left, a.Right, b.Left, b.Right) <= reach) ||
        (a.HorizontalOverlapRatio(b) >= 0.3 && RectI.IntervalGap(a.Top, a.Bottom, b.Top, b.Bottom) <= reach);

    private static void AddPacked(List<RectI[]> candidates, VisibleWindow[] windows, RectI[] original,
        RectI area, int gap, int columns, bool equalize)
    {
        var horizontal = columns == windows.Length;
        var order = Enumerable.Range(0, windows.Length);
        var sorted = horizontal
            ? order.OrderBy(i => CenterX(original[i])).ThenBy(i => i).ToArray()
            : order.OrderBy(i => CenterY(original[i])).ThenBy(i => CenterX(original[i])).ToArray();
        var rows = sorted.Chunk(columns).Select(row => row.OrderBy(i => CenterX(original[i])).ToArray()).ToArray();
        var sizes = original.ToArray();
        if (equalize)
        {
            var median = original.Select(r => r.Height).Order().ElementAt(original.Length / 2);
            for (var i = 0; i < windows.Length; i++)
                if (windows[i].Window.IsResizable && Math.Abs(median - sizes[i].Height) <= sizes[i].Height * 0.12)
                    sizes[i] = sizes[i] with { Height = median };
        }
        foreach (var row in rows)
        {
            var width = row.Sum(i => sizes[i].Width) + gap * (row.Length - 1);
            if (width <= area.Width) continue;
            var fixedWidth = row.Where(i => !windows[i].Window.IsResizable).Sum(i => sizes[i].Width);
            var flexibleWidth = row.Where(i => windows[i].Window.IsResizable).Sum(i => sizes[i].Width);
            var available = area.Width - fixedWidth - gap * (row.Length - 1);
            if (flexibleWidth == 0 || available <= 0) return;
            var ratio = available / (double)flexibleWidth;
            if (ratio < 0.75) return;
            foreach (var i in row.Where(i => windows[i].Window.IsResizable))
                sizes[i] = sizes[i] with { Width = (int)Math.Floor(sizes[i].Width * ratio) };
        }
        var rowHeights = rows.Select(row => row.Max(i => sizes[i].Height)).ToArray();
        var totalHeight = rowHeights.Sum() + gap * (rows.Length - 1);
        if (totalHeight > area.Height) return;
        var totalWidth = rows.Max(row => row.Sum(i => sizes[i].Width) + gap * (row.Length - 1));
        var bounds = Bounds(original);
        var left = Math.Clamp((int)Math.Round(CenterX(bounds) - totalWidth / 2.0), area.Left, area.Right - totalWidth);
        var top = Math.Clamp((int)Math.Round(CenterY(bounds) - totalHeight / 2.0), area.Top, area.Bottom - totalHeight);
        var edgeRadius = (int)Math.Round(24 * windows[0].Window.Dpi / 96.0);
        if (Math.Abs(bounds.Left - area.Left) <= edgeRadius) left = area.Left;
        else if (Math.Abs(bounds.Right - area.Right) <= edgeRadius) left = area.Right - totalWidth;
        if (Math.Abs(bounds.Top - area.Top) <= edgeRadius) top = area.Top;
        else if (Math.Abs(bounds.Bottom - area.Bottom) <= edgeRadius) top = area.Bottom - totalHeight;
        var target = new RectI[windows.Length];
        var y = top;
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var x = left;
            foreach (var i in rows[rowIndex])
            {
                target[i] = new RectI(x, y, sizes[i].Width, sizes[i].Height);
                x += sizes[i].Width + gap;
            }
            y += rowHeights[rowIndex] + gap;
        }
        candidates.Add(target);
    }

    private static bool HitsOutsideGroup(IReadOnlyDictionary<nint, RectI> settled, VisibleWindow[] group, RectI[] original, RectI[] target)
    {
        var handles = group.Select(v => v.Window.Handle).ToHashSet();
        foreach (var other in settled.Where(v => !handles.Contains(v.Key)))
            for (var i = 0; i < group.Length; i++)
                if (target[i].Intersect(other.Value).Area > original[i].Intersect(other.Value).Area)
                    return true;
        return false;
    }

    private static RectI Fit(RectI rect, WindowSnapshot window, TidyOptions options)
    {
        if (!options.RescueOffscreenWindows || window.WorkArea.IsEmpty) return rect;
        var area = window.WorkArea;
        var width = window.IsResizable ? Math.Min(rect.Width, area.Width) : rect.Width;
        var height = window.IsResizable ? Math.Min(rect.Height, area.Height) : rect.Height;
        return new RectI(width <= area.Width ? Math.Clamp(rect.Left, area.Left, area.Right - width) : area.Left,
            height <= area.Height ? Math.Clamp(rect.Top, area.Top, area.Bottom - height) : area.Top, width, height);
    }

    private static RectI Bounds(RectI[] rects) => RectI.FromEdges(rects.Min(r => r.Left), rects.Min(r => r.Top), rects.Max(r => r.Right), rects.Max(r => r.Bottom));
    private static double CenterX(RectI r) => r.Left + r.Width / 2.0;
    private static double CenterY(RectI r) => r.Top + r.Height / 2.0;
}

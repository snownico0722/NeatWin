namespace NeatWin.Core;

/// <summary>
/// Arrange nearby working groups, not the entire monitor. Floating size and coarse spatial order
/// are context, not a perfect target. Explicit tidy may resolve substantial accidental overlap;
/// follow-hand assistance remains a separate small correction and never invokes this planner.
/// </summary>
internal static partial class IntentLayoutPlanner
{
    internal static IReadOnlyList<TidyMove> CreatePlan(IReadOnlyList<VisibleWindow> visible,
        TidyOptions options, IntentReferenceDocument? reference = null) =>
        CreateDetailedPlan(visible, options, reference).Moves;

    internal static IntentLayoutPlan CreateDetailedPlan(IReadOnlyList<VisibleWindow> visible,
        TidyOptions options, IntentReferenceDocument? reference = null,
        IReadOnlyList<WindowSnapshot>? desktop = null)
    {
        if (options.AlgorithmMode == TidyAlgorithmMode.Classic)
            return new(new TidyEngine().CreatePlan(visible, options), [], []);
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
                var original = group.Select(v => Fit(v.Window.VisualRect, v.Window, options)).ToArray();
                var order = Enumerable.Range(0, group.Length).ToArray();
                var blockers = desktop?.Where(w => !all.Any(v => v.Window.Handle == w.Handle) &&
                    w.MonitorHandle == group[0].Window.MonitorHandle && w.ZOrder < group.Max(v => v.Window.ZOrder)).ToArray() ?? [];
                var generationNotes = new List<LayoutCandidateTrace>();
                var candidates = new List<Candidate> { new("keep", original, order) };
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
                }
                var stackSignal = StackSignal(group, original);
                double Cost(Candidate c) => Score(group, original, c.Rects, hint, options) +
                    OcclusionCost(group, original, c, stackSignal, hint);
                var best = candidates[0]; var baseline = Cost(best); var bestScore = baseline;
                var audits = new List<LayoutCandidateTrace>(generationNotes) { new("keep", baseline, null) };
                foreach (var candidate in candidates.Skip(1))
                {
                    var rejection = !candidate.Order.SequenceEqual(order) &&
                        !WindowLayerSafety.CanReorder(group.Select(v => v.Window).ToArray(), desktop ?? all.Select(v => v.Window).ToArray())
                        ? "layer-band-or-interleaved-window" : !Safe(group, original, candidate.Rects, area, options) ? "geometry-budget" :
                        HitsOutsideGroup(settled, group, original, candidate.Rects) ? "other-group" :
                        blockers.Any(w => candidate.Rects.Select((r, i) => r.Intersect(w.VisualRect).Area > original[i].Intersect(w.VisualRect).Area).Any(b => b)) ? "fixed-occluder" :
                        !ExposureSafe(group, original, candidate) ? "exposure-or-access" : null;
                    if (rejection is not null) { audits.Add(new(candidate.Kind, null, rejection)); continue; }
                    var score = Cost(candidate);
                    audits.Add(new(candidate.Kind, score, null));
                    var threshold = options.SmartStrength switch
                    {
                        SmartTidyStrength.Gentle => 0.28,
                        SmartTidyStrength.Assertive => 0.035,
                        _ => 0.10,
                    };
                    if (score < bestScore && score < baseline - threshold) { best = candidate; bestScore = score; }
                }
                for (var i = 0; i < group.Length; i++)
                {
                    settled[group[i].Window.Handle] = best.Rects[i];
                    if (best.Rects[i] != group[i].Window.VisualRect) moves.Add(new(group[i].Window, best.Rects[i]));
                }
                if (!best.Order.SequenceEqual(order)) layers.Add(new(best.Order.Select(i => group[i].Window).ToArray()));
                traces.Add(new(group.Select(v => v.Window.Handle).ToArray(), best.Kind, stackSignal, audits.ToArray()));

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

    private static bool ExposureSafe(VisibleWindow[] windows, RectI[] original, Candidate candidate)
    {
        var front = new List<RectI>();
        foreach (var i in candidate.Order)
        {
            var before = OcclusionMetrics.Measure(original[i], original.Take(i), windows[i].Window.Dpi);
            var after = OcclusionMetrics.Measure(candidate.Rects[i], front, windows[i].Window.Dpi);
            if (candidate.Kind == "edge-row" && after.CenterVisible < 0.92) return false;
            if (after.Visible < Math.Min(0.24, before.Visible) - 0.01 ||
                (after.AccessWidth < Math.Min(100 * windows[i].Window.Dpi / 96.0, before.AccessWidth) &&
                 after.UsefulWidth < Math.Min(240 * windows[i].Window.Dpi / 96.0, before.UsefulWidth))) return false;
            front.Add(candidate.Rects[i]);
        }
        return true;
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
        // Only shrink to make the local group fit. Never expand two windows to fill an ultrawide.
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

    private static double Score(VisibleWindow[] windows, RectI[] original, RectI[] target,
        IntentHint hint, TidyOptions options)
    {
        var cost = 0.0;
        var pairs = 0;
        for (var i = 0; i < windows.Length; i++)
        for (var j = i + 1; j < windows.Length; j++)
        {
            var a = target[i]; var b = target[j];
            var oa = original[i]; var ob = original[j];
            if (!Related(oa, ob, 260) && !Related(a, b, 260)) continue;
            pairs++;
            // Occlusion is evaluated as a visible-region union, with the candidate layer order.
            var horizontal = a.VerticalOverlapRatio(b) >= 0.3;
            var vertical = a.HorizontalOverlapRatio(b) >= 0.3;
            if (horizontal && vertical)
            {
                var x = Math.Abs(CenterX(a) - CenterX(b)) / Math.Max(1, Math.Min(a.Width, b.Width));
                var y = Math.Abs(CenterY(a) - CenterY(b)) / Math.Max(1, Math.Min(a.Height, b.Height));
                horizontal = x >= y; vertical = !horizontal;
            }
            if (horizontal)
            {
                cost += Math.Min(1, Math.Min(Math.Abs(a.Top - b.Top), Math.Abs(a.Bottom - b.Bottom)) / 80.0);
                var gap = CenterX(a) <= CenterX(b) ? b.Left - a.Right : a.Left - b.Right;
                if (gap >= 0) cost += 1.8 * Math.Min(1, Math.Abs(gap - hint.GapPixels) / 160.0);
            }
            if (vertical)
            {
                cost += Math.Min(1, Math.Min(Math.Abs(a.Left - b.Left), Math.Abs(a.Right - b.Right)) / 80.0);
                var gap = CenterY(a) <= CenterY(b) ? b.Top - a.Bottom : a.Top - b.Bottom;
                if (gap >= 0) cost += 1.8 * Math.Min(1, Math.Abs(gap - hint.GapPixels) / 160.0);
            }
            var dx = CenterX(oa) - CenterX(ob); var dy = CenterY(oa) - CenterY(ob);
            if (Math.Abs(dx) > Math.Abs(dy) * 1.4 && dx * (CenterX(a) - CenterX(b)) < 0) cost += 4;
            if (Math.Abs(dy) > Math.Abs(dx) * 1.4 && dy * (CenterY(a) - CenterY(b)) < 0) cost += 4;
            // An intentionally tiny tie-breaker, never a learned layout veto.
            cost -= 0.08 * hint.Confidence * hint.HorizontalPreference * (horizontal ? 1 : -1);
        }
        cost /= Math.Max(1, pairs);
        if (windows.Length == 1)
        {
            var area = windows[0].Window.WorkArea; var a = original[0]; var b = target[0];
            if (Math.Abs(a.Left - area.Left) <= 48) cost += Math.Abs(b.Left - area.Left) / 24.0;
            if (Math.Abs(a.Top - area.Top) <= 48) cost += Math.Abs(b.Top - area.Top) / 24.0;
        }
        for (var i = 0; i < windows.Length; i++)
        {
            var a = original[i]; var b = target[i];
            var distance = Math.Sqrt(Math.Pow(CenterX(a) - CenterX(b), 2) + Math.Pow(CenterY(a) - CenterY(b), 2));
            var resistance = options.SmartStrength switch { SmartTidyStrength.Gentle => 2.2, SmartTidyStrength.Assertive => 0.75, _ => 1.2 };
            cost += resistance * (windows[i].Window.IsForeground ? 1.4 : 1) * distance /
                Math.Max(200, Math.Min(a.Width, a.Height)) / windows.Length;
            cost += 0.8 * (Math.Abs(Math.Log(b.Width / (double)Math.Max(1, a.Width))) +
                Math.Abs(Math.Log(b.Height / (double)Math.Max(1, a.Height)))) / windows.Length;
        }
        return cost;
    }

    private static bool Safe(VisibleWindow[] windows, RectI[] original, RectI[] target, RectI area, TidyOptions options)
    {
        var fraction = options.SmartStrength switch { SmartTidyStrength.Gentle => 0.08, SmartTidyStrength.Assertive => 0.30, _ => 0.20 };
        var budget = Math.Sqrt((double)area.Width * area.Width + (double)area.Height * area.Height) * fraction;
        for (var i = 0; i < windows.Length; i++)
        {
            var a = original[i]; var b = target[i];
            if (b.IsEmpty || b.Intersect(area).Area != b.Area) return false;
            if (!windows[i].Window.IsResizable && (b.Width != a.Width || b.Height != a.Height)) return false;
            if (b.Width < Math.Min(a.Width, options.MinimumWidth) || b.Height < Math.Min(a.Height, options.MinimumHeight)) return false;
            if (b.Width < a.Width * 0.75 || b.Width > a.Width * 1.20 || b.Height < a.Height * 0.75 || b.Height > a.Height * 1.20) return false;
            if (Math.Abs(CenterX(a) - CenterX(b)) > budget || Math.Abs(CenterY(a) - CenterY(b)) > budget) return false;
        }
        return true;
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

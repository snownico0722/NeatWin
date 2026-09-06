namespace NeatWin.Core;

/// <summary>
/// Arrange nearby working groups, not the entire monitor. Floating size and coarse spatial order
/// are context, not a perfect target. Explicit tidy may resolve substantial accidental overlap;
/// follow-hand assistance remains a separate small correction and never invokes this planner.
/// </summary>
internal static class IntentLayoutPlanner
{
    internal static IReadOnlyList<TidyMove> CreatePlan(IReadOnlyList<VisibleWindow> visible,
        TidyOptions options, IntentReferenceDocument? reference = null)
    {
        if (options.AlgorithmMode == TidyAlgorithmMode.Classic)
            return new TidyEngine().CreatePlan(visible, options);
        var moves = new List<TidyMove>();
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
                var candidates = new List<RectI[]> { original };
                // Retain the previous version's useful exact local corrections as a candidate.
                var local = new TidyEngine().CreatePlan(group, options).ToDictionary(m => m.Window.Handle, m => m.TargetVisualRect);
                candidates.Add(group.Select((v, i) => local.GetValueOrDefault(v.Window.Handle, original[i])).ToArray());
                if (group.Length is >= 2 and <= 12)
                {
                    AddPacked(candidates, group, original, area, hint.GapPixels, columns: group.Length, equalize: false);
                    AddPacked(candidates, group, original, area, hint.GapPixels, columns: group.Length, equalize: true);
                    AddPacked(candidates, group, original, area, hint.GapPixels, columns: 1, equalize: false);
                    if (group.Length >= 3)
                        for (var columns = 2; columns < group.Length; columns++)
                            AddPacked(candidates, group, original, area, hint.GapPixels, columns, equalize: false);
                }
                var baseline = Score(group, original, original, hint, options);
                var bestScore = baseline;
                var best = original;
                foreach (var candidate in candidates.Skip(1))
                {
                    if (!Safe(group, original, candidate, area, options) ||
                        HitsOutsideGroup(settled, group, original, candidate)) continue;
                    var score = Score(group, original, candidate, hint, options);
                    var threshold = options.SmartStrength switch
                    {
                        SmartTidyStrength.Gentle => 0.28,
                        SmartTidyStrength.Assertive => 0.035,
                        _ => 0.10,
                    };
                    if (score < bestScore && score < baseline - threshold)
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }
                for (var i = 0; i < group.Length; i++)
                {
                    settled[group[i].Window.Handle] = best[i];
                    if (best[i] != group[i].Window.VisualRect)
                        moves.Add(new TidyMove(group[i].Window, best[i]));
                }
            }
        }
        return moves;
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
            cost += 8.0 * a.Intersect(b).Area / Math.Max(1.0, Math.Min(a.Area, b.Area));
            var horizontal = a.VerticalOverlapRatio(b) >= 0.3;
            var vertical = a.HorizontalOverlapRatio(b) >= 0.3;
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
        for (var i = 0; i < windows.Length; i++)
            for (var j = i + 1; j < windows.Length; j++)
                if (target[i].Intersect(target[j]).Area > original[i].Intersect(original[j]).Area) return false;
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

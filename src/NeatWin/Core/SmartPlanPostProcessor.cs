namespace NeatWin.Core;

/// <summary>
/// Applies user-facing Smart behavior preferences after the constraint solver has produced its
/// cosmetic plan. This layer deliberately contains only high-level behaviors that are easier to
/// express as a projection than as another public solver coefficient: reversible vertical fill
/// preference and overlap separation.
/// </summary>
public static class SmartPlanPostProcessor
{
    public static IReadOnlyList<TidyMove> Refine(
        IReadOnlyList<VisibleWindow> visibleWindows,
        IReadOnlyList<TidyMove> basePlan,
        TidyOptions tidyOptions,
        SmartBehaviorOptions behaviorOptions)
    {
        if (tidyOptions.AlgorithmMode != TidyAlgorithmMode.Smart || visibleWindows.Count == 0)
        {
            return basePlan;
        }

        var targetByHandle = basePlan.ToDictionary(
            static move => move.Window.Handle,
            static move => move.TargetVisualRect);
        var result = new List<TidyMove>();

        foreach (var monitorGroup in visibleWindows.GroupBy(static item => item.Window.MonitorHandle))
        {
            var states = monitorGroup
                .Select(item => new State(
                    item,
                    targetByHandle.TryGetValue(item.Window.Handle, out var target)
                        ? target
                        : item.Window.VisualRect))
                .ToList();

            if (behaviorOptions.PreferReversibleVerticalFill)
            {
                ApplyVerticalFillPreference(states, tidyOptions);
            }

            ApplyOverlapAvoidance(states, tidyOptions, behaviorOptions.OverlapAvoidance);

            foreach (var state in states)
            {
                var target = tidyOptions.RescueOffscreenWindows
                    ? FitInsideWorkArea(state.Snapshot, state.Current)
                    : state.Current;

                if (HasMeaningfulChange(state.Snapshot.VisualRect, target))
                {
                    result.Add(new TidyMove(state.Snapshot, target));
                }
            }
        }

        return result;
    }

    private static void ApplyVerticalFillPreference(List<State> states, TidyOptions options)
    {
        var minimumHeightRatio = options.SmartStrength switch
        {
            SmartTidyStrength.Gentle => 0.84,
            SmartTidyStrength.Assertive => 0.62,
            _ => 0.72,
        };
        var edgeRadius = options.SmartHitTendency switch
        {
            SmartHitTendency.Cautious => 56,
            SmartHitTendency.Sensitive => 156,
            _ => 104,
        };
        var maximumGrowthRatio = options.SmartSizeTendency switch
        {
            SmartSizeTendency.Preserve => 0.08,
            SmartSizeTendency.Expand => 0.34,
            _ => 0.20,
        };

        foreach (var state in states)
        {
            if (!state.Snapshot.IsResizable)
            {
                continue;
            }

            var workArea = state.Snapshot.WorkArea;
            var current = state.Current;
            if (workArea.IsEmpty || current.IsEmpty || current.Height <= 0)
            {
                continue;
            }

            if (Math.Abs(current.Top - workArea.Top) <= 1 &&
                Math.Abs(current.Bottom - workArea.Bottom) <= 1)
            {
                continue;
            }

            var heightRatio = (double)current.Height / workArea.Height;
            if (heightRatio < minimumHeightRatio)
            {
                continue;
            }

            var topDistance = Math.Abs(current.Top - workArea.Top);
            var bottomDistance = Math.Abs(workArea.Bottom - current.Bottom);
            var bothEdgesPlausible = topDistance <= edgeRadius && bottomDistance <= edgeRadius;
            var oneEdgeStrong =
                (topDistance <= edgeRadius / 2 || bottomDistance <= edgeRadius / 2) &&
                Math.Max(topDistance, bottomDistance) <= edgeRadius * 1.5;
            if (!bothEdgesPlausible && !oneEdgeStrong)
            {
                continue;
            }

            var growthRatio = Math.Max(0, (double)(workArea.Height - current.Height) / current.Height);
            if (growthRatio > maximumGrowthRatio)
            {
                continue;
            }

            state.Current = RectI.FromEdges(
                current.Left,
                workArea.Top,
                current.Right,
                workArea.Bottom);
        }
    }

    private static void ApplyOverlapAvoidance(
        List<State> states,
        TidyOptions options,
        SmartOverlapAvoidance level)
    {
        if (states.Count < 2)
        {
            return;
        }

        var profile = level switch
        {
            SmartOverlapAvoidance.Gentle => new OverlapProfile(0.12, 2, 72),
            SmartOverlapAvoidance.Strong => new OverlapProfile(1.00, 8, 320),
            _ => new OverlapProfile(0.36, 5, 176),
        };

        var strengthBudget = options.SmartStrength switch
        {
            SmartTidyStrength.Gentle => 64,
            SmartTidyStrength.Assertive => 144,
            _ => 96,
        };
        var movementBudget = Math.Max(strengthBudget, profile.MinimumMovementBudget);

        for (var pass = 0; pass < profile.Passes; pass++)
        {
            var changed = false;

            for (var i = 0; i < states.Count; i++)
            {
                for (var j = i + 1; j < states.Count; j++)
                {
                    var a = states[i];
                    var b = states[j];
                    var overlapX = Math.Min(a.Current.Right, b.Current.Right) - Math.Max(a.Current.Left, b.Current.Left);
                    var overlapY = Math.Min(a.Current.Bottom, b.Current.Bottom) - Math.Max(a.Current.Top, b.Current.Top);
                    if (overlapX <= 0 || overlapY <= 0)
                    {
                        continue;
                    }

                    var smallerArea = Math.Min(a.Current.Area, b.Current.Area);
                    var overlapArea = (long)overlapX * overlapY;
                    var overlapRatio = smallerArea <= 0 ? 0 : (double)overlapArea / smallerArea;
                    if (overlapRatio > profile.MaximumOverlapAreaRatio)
                    {
                        continue;
                    }

                    var normalizedX = (double)overlapX / Math.Max(1, Math.Min(a.Current.Width, b.Current.Width));
                    var normalizedY = (double)overlapY / Math.Max(1, Math.Min(a.Current.Height, b.Current.Height));

                    if (normalizedX <= normalizedY)
                    {
                        changed |= SeparateHorizontally(a, b, overlapX, movementBudget, options.RescueOffscreenWindows);
                    }
                    else
                    {
                        changed |= SeparateVertically(a, b, overlapY, movementBudget, options.RescueOffscreenWindows);
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    private static bool SeparateHorizontally(
        State a,
        State b,
        int overlap,
        int movementBudget,
        bool keepInsideWorkArea)
    {
        var aBeforeB = CenterX(a.Current) <= CenterX(b.Current);
        var left = aBeforeB ? a : b;
        var right = aBeforeB ? b : a;
        var total = overlap + 1;
        SplitMovement(left, right, total, out var leftShare, out var rightShare);

        var beforeLeft = left.Current;
        var beforeRight = right.Current;
        left.TranslateX(-leftShare, movementBudget);
        right.TranslateX(rightShare, movementBudget);

        if (keepInsideWorkArea)
        {
            left.Current = FitInsideWorkArea(left.Snapshot, left.Current);
            right.Current = FitInsideWorkArea(right.Snapshot, right.Current);
        }

        return left.Current != beforeLeft || right.Current != beforeRight;
    }

    private static bool SeparateVertically(
        State a,
        State b,
        int overlap,
        int movementBudget,
        bool keepInsideWorkArea)
    {
        var aBeforeB = CenterY(a.Current) <= CenterY(b.Current);
        var top = aBeforeB ? a : b;
        var bottom = aBeforeB ? b : a;
        var total = overlap + 1;
        SplitMovement(top, bottom, total, out var topShare, out var bottomShare);

        var beforeTop = top.Current;
        var beforeBottom = bottom.Current;
        top.TranslateY(-topShare, movementBudget);
        bottom.TranslateY(bottomShare, movementBudget);

        if (keepInsideWorkArea)
        {
            top.Current = FitInsideWorkArea(top.Snapshot, top.Current);
            bottom.Current = FitInsideWorkArea(bottom.Snapshot, bottom.Current);
        }

        return top.Current != beforeTop || bottom.Current != beforeBottom;
    }

    private static void SplitMovement(
        State first,
        State second,
        int total,
        out int firstShare,
        out int secondShare)
    {
        var firstMobility = 1.0 / Math.Max(0.1, first.Importance);
        var secondMobility = 1.0 / Math.Max(0.1, second.Importance);
        var sum = firstMobility + secondMobility;
        firstShare = (int)Math.Round(total * firstMobility / sum);
        secondShare = total - firstShare;
    }

    private static RectI FitInsideWorkArea(WindowSnapshot snapshot, RectI rect)
    {
        var workArea = snapshot.WorkArea;
        if (workArea.IsEmpty || rect.IsEmpty)
        {
            return rect;
        }

        var width = snapshot.IsResizable ? Math.Min(rect.Width, workArea.Width) : rect.Width;
        var height = snapshot.IsResizable ? Math.Min(rect.Height, workArea.Height) : rect.Height;
        var left = width <= workArea.Width
            ? Math.Clamp(rect.Left, workArea.Left, workArea.Right - width)
            : workArea.Left;
        var top = height <= workArea.Height
            ? Math.Clamp(rect.Top, workArea.Top, workArea.Bottom - height)
            : workArea.Top;
        return new RectI(left, top, width, height);
    }

    private static double CenterX(RectI rect) => (rect.Left + rect.Right) / 2.0;
    private static double CenterY(RectI rect) => (rect.Top + rect.Bottom) / 2.0;

    private static bool HasMeaningfulChange(RectI original, RectI target) =>
        Math.Abs(original.Left - target.Left) >= 2 ||
        Math.Abs(original.Top - target.Top) >= 2 ||
        Math.Abs(original.Right - target.Right) >= 2 ||
        Math.Abs(original.Bottom - target.Bottom) >= 2;

    private sealed class State
    {
        internal State(VisibleWindow visible, RectI current)
        {
            Snapshot = visible.Window;
            Original = visible.Window.VisualRect;
            Current = current;
            Importance = 0.80 + (0.45 * Math.Clamp(visible.VisibleRatio, 0, 1));
            if (Snapshot.IsForeground)
            {
                Importance += 0.95;
            }
        }

        internal WindowSnapshot Snapshot { get; }
        internal RectI Original { get; }
        internal RectI Current { get; set; }
        internal double Importance { get; }

        internal void TranslateX(int delta, int budget)
        {
            var desired = Current.X + delta;
            var clamped = Math.Clamp(desired, Original.X - budget, Original.X + budget);
            Current = new RectI(clamped, Current.Y, Current.Width, Current.Height);
        }

        internal void TranslateY(int delta, int budget)
        {
            var desired = Current.Y + delta;
            var clamped = Math.Clamp(desired, Original.Y - budget, Original.Y + budget);
            Current = new RectI(Current.X, clamped, Current.Width, Current.Height);
        }
    }

    private readonly record struct OverlapProfile(
        double MaximumOverlapAreaRatio,
        int Passes,
        int MinimumMovementBudget);
}

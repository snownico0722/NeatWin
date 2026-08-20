using NeatWin.Core;

namespace NeatWin.Tests;

public sealed class CoreBehaviorTests
{
    private static readonly RectI WorkArea = new(0, 0, 1920, 1080);

    [Fact]
    public void VisibilityAnalyzer_ExcludesFullyOccludedBackgroundWindow()
    {
        var front = Window(1, new RectI(0, 0, 1000, 1000), 0);
        var back = Window(2, new RectI(0, 0, 1000, 1000), 1);

        var result = new VisibilityAnalyzer().SelectVisibleWorkingSet([front, back]);

        var visible = Assert.Single(result);
        Assert.Equal(front.Handle, visible.Window.Handle);
    }

    [Fact]
    public void VisibilityAnalyzer_ExcludesWindowWithOnlyTinyStripVisible()
    {
        var front = Window(1, new RectI(0, 0, 950, 1000), 0);
        var back = Window(2, new RectI(0, 0, 1000, 1000), 1);

        var result = new VisibilityAnalyzer().SelectVisibleWorkingSet([front, back]);

        Assert.DoesNotContain(result, item => item.Window.Handle == back.Handle);
    }

    [Fact]
    public void VisibilityAnalyzer_IncludesMeaningfullyExposedBackgroundWindow()
    {
        var front = Window(1, new RectI(0, 0, 600, 1000), 0);
        var back = Window(2, new RectI(0, 0, 1000, 1000), 1);

        var result = new VisibilityAnalyzer().SelectVisibleWorkingSet([front, back]);

        Assert.Contains(result, item => item.Window.Handle == back.Handle);
    }

    [Fact]
    public void VisibilityAnalyzer_ExcludesFullyOffscreenWindow()
    {
        var offscreen = Window(1, new RectI(-900, 100, 500, 500), 0, foreground: true);

        var result = new VisibilityAnalyzer().SelectVisibleWorkingSet([offscreen]);

        Assert.Empty(result);
    }

    [Fact]
    public void VisibilityAnalyzer_IncludesPartiallyOffscreenForegroundWindow()
    {
        var offscreen = Window(1, new RectI(-400, 100, 700, 500), 0, foreground: true);

        var result = new VisibilityAnalyzer().SelectVisibleWorkingSet([offscreen]);

        Assert.Single(result);
    }

    [Fact]
    public void TidyOptions_DefaultToSmartBalancedProfile()
    {
        var options = new TidyOptions();

        Assert.Equal(TidyAlgorithmMode.Smart, options.AlgorithmMode);
        Assert.Equal(SmartTidyStrength.Balanced, options.SmartStrength);
    }

    [Fact]
    public void TidyEngine_ClosesSmallHorizontalGapWithoutResizing()
    {
        var left = Window(1, new RectI(200, 180, 600, 520), 0, foreground: true);
        var right = Window(2, new RectI(830, 180, 620, 520), 1);

        var plan = new TidyEngine().CreatePlan([Visible(left), Visible(right)]);
        var leftTarget = TargetFor(plan, left);
        var rightTarget = TargetFor(plan, right);

        Assert.InRange(Math.Abs(leftTarget.Right - rightTarget.Left), 0, 1);
        Assert.Equal(left.VisualRect.Width, leftTarget.Width);
        Assert.Equal(left.VisualRect.Height, leftTarget.Height);
        Assert.Equal(right.VisualRect.Width, rightTarget.Width);
        Assert.Equal(right.VisualRect.Height, rightTarget.Height);
        Assert.True(Math.Abs(leftTarget.X - left.VisualRect.X) < Math.Abs(rightTarget.X - right.VisualRect.X));
    }

    [Fact]
    public void TidyEngine_RemovesSmallHorizontalOverlapWithoutResizing()
    {
        var left = Window(1, new RectI(200, 180, 600, 520), 0);
        var right = Window(2, new RectI(780, 180, 620, 520), 1);

        var plan = new TidyEngine().CreatePlan([Visible(left), Visible(right)]);
        var leftTarget = TargetFor(plan, left);
        var rightTarget = TargetFor(plan, right);

        Assert.InRange(Math.Abs(leftTarget.Right - rightTarget.Left), 0, 1);
        Assert.Equal(left.VisualRect.Width, leftTarget.Width);
        Assert.Equal(left.VisualRect.Height, leftTarget.Height);
        Assert.Equal(right.VisualRect.Width, rightTarget.Width);
        Assert.Equal(right.VisualRect.Height, rightTarget.Height);
    }

    [Fact]
    public void TidyEngine_SnapsClearlyNearbyScreenEdgeByTranslation()
    {
        var window = Window(1, new RectI(18, 220, 600, 500), 0);

        var plan = new TidyEngine().CreatePlan([Visible(window)]);
        var target = TargetFor(plan, window);

        Assert.Equal(WorkArea.Left, target.Left);
        Assert.Equal(window.VisualRect.Width, target.Width);
        Assert.Equal(window.VisualRect.Height, target.Height);
        Assert.Equal(window.VisualRect.Top, target.Top);
    }

    [Fact]
    public void TidyEngine_DoesNotStretchNearFullWindowToFillScreen()
    {
        var window = Window(1, new RectI(18, 180, 1884, 650), 0);

        var plan = new TidyEngine().CreatePlan([Visible(window)]);
        var target = TargetFor(plan, window);

        Assert.Equal(window.VisualRect, target);
    }

    [Fact]
    public void TidyEngine_DoesNotPullDistantWindowsTogether()
    {
        var left = Window(1, new RectI(200, 200, 500, 500), 0);
        var right = Window(2, new RectI(1200, 200, 500, 500), 1);

        var plan = new TidyEngine().CreatePlan([Visible(left), Visible(right)]);

        Assert.Empty(plan);
    }

    [Fact]
    public void TidyEngine_RescuesWindowFarOutsideWorkAreaBeyondSmartGentleBudget()
    {
        var window = Window(1, new RectI(-420, 180, 700, 500), 0);
        var options = new TidyOptions(SmartStrength: SmartTidyStrength.Gentle);

        var plan = new TidyEngine().CreatePlan([Visible(window)], options);
        var target = TargetFor(plan, window);

        Assert.Equal(0, target.Left);
        Assert.Equal(700, target.Width);
        Assert.Equal(180, target.Top);
    }

    [Fact]
    public void TidyEngine_RescuesRightAndBottomOverflow()
    {
        var window = Window(1, new RectI(1700, 900, 600, 400), 0);

        var plan = new TidyEngine().CreatePlan([Visible(window)]);
        var target = TargetFor(plan, window);

        Assert.Equal(1320, target.Left);
        Assert.Equal(680, target.Top);
        Assert.Equal(1920, target.Right);
        Assert.Equal(1080, target.Bottom);
    }

    [Fact]
    public void TidyEngine_ShrinksOversizedResizableWindowToWorkArea()
    {
        var window = Window(1, new RectI(-300, -200, 2500, 1400), 0);

        var plan = new TidyEngine().CreatePlan([Visible(window)]);
        var target = TargetFor(plan, window);

        Assert.Equal(WorkArea, target);
    }

    [Fact]
    public void TidyEngine_CanDisableOffscreenRescue()
    {
        var window = Window(1, new RectI(-400, 200, 600, 500), 0);
        var options = new TidyOptions(RescueOffscreenWindows: false);

        var plan = new TidyEngine().CreatePlan([Visible(window)], options);

        Assert.Empty(plan);
    }

    [Fact]
    public void ClassicMode_RespectsCustomNeighborSnapDistance()
    {
        var left = Window(1, new RectI(200, 200, 500, 500), 0);
        var right = Window(2, new RectI(740, 200, 500, 500), 1);
        var conservative = new TidyOptions(
            AlgorithmMode: TidyAlgorithmMode.Classic,
            NeighborSnapDistance: 20,
            AlignmentSnapDistance: 0,
            ScreenSnapDistance: 0,
            MaximumEdgeAdjustment: 200,
            RescueOffscreenWindows: false,
            Passes: 1);
        var generous = conservative with { NeighborSnapDistance = 50 };

        var conservativePlan = new TidyEngine().CreatePlan([Visible(left), Visible(right)], conservative);
        var generousPlan = new TidyEngine().CreatePlan([Visible(left), Visible(right)], generous);

        Assert.Empty(conservativePlan);
        Assert.NotEmpty(generousPlan);
        Assert.Equal(TargetFor(generousPlan, left).Right, TargetFor(generousPlan, right).Left);
    }

    [Fact]
    public void SmartMode_LegacySplitKnobsDoNotChangeUnifiedProfile()
    {
        var left = Window(1, new RectI(200, 200, 500, 500), 0);
        var right = Window(2, new RectI(730, 200, 500, 500), 1);
        var first = new TidyOptions(
            SmartStrength: SmartTidyStrength.Balanced,
            SmartHitTendency: SmartHitTendency.Cautious,
            SmartSizeTendency: SmartSizeTendency.Preserve,
            RescueOffscreenWindows: false);
        var second = first with
        {
            SmartHitTendency = SmartHitTendency.Sensitive,
            SmartSizeTendency = SmartSizeTendency.Expand,
        };

        var firstPlan = new TidyEngine().CreatePlan([Visible(left), Visible(right)], first);
        var secondPlan = new TidyEngine().CreatePlan([Visible(left), Visible(right)], second);

        Assert.Equal(TargetFor(firstPlan, left), TargetFor(secondPlan, left));
        Assert.Equal(TargetFor(firstPlan, right), TargetFor(secondPlan, right));
    }

    [Fact]
    public void SmartMode_IgnoresClassicRawThresholds()
    {
        var left = Window(1, new RectI(200, 200, 500, 500), 0);
        var right = Window(2, new RectI(780, 200, 500, 500), 1);
        var options = new TidyOptions(
            AlgorithmMode: TidyAlgorithmMode.Smart,
            SmartStrength: SmartTidyStrength.Balanced,
            NeighborSnapDistance: 240,
            AlignmentSnapDistance: 120,
            ScreenSnapDistance: 240,
            RescueOffscreenWindows: false);

        var plan = new TidyEngine().CreatePlan([Visible(left), Visible(right)], options);

        Assert.Empty(plan);
    }

    [Fact]
    public void SmartMode_OverallTendencyControlsRelationReach()
    {
        var left = Window(1, new RectI(200, 200, 500, 500), 0);
        var right = Window(2, new RectI(750, 200, 500, 500), 1);
        var gentle = new TidyOptions(
            SmartStrength: SmartTidyStrength.Gentle,
            RescueOffscreenWindows: false);
        var assertive = gentle with { SmartStrength = SmartTidyStrength.Assertive };

        var gentlePlan = new TidyEngine().CreatePlan([Visible(left), Visible(right)], gentle);
        var assertivePlan = new TidyEngine().CreatePlan([Visible(left), Visible(right)], assertive);

        Assert.Empty(gentlePlan);
        Assert.NotEmpty(assertivePlan);
        Assert.InRange(
            Math.Abs(TargetFor(assertivePlan, left).Right - TargetFor(assertivePlan, right).Left),
            0,
            1);
    }

    [Fact]
    public void SmartMode_OverallTendencyControlsScreenReach()
    {
        var window = Window(1, new RectI(30, 220, 600, 500), 0);
        var gentle = new TidyOptions(
            SmartStrength: SmartTidyStrength.Gentle,
            RescueOffscreenWindows: false);
        var assertive = gentle with { SmartStrength = SmartTidyStrength.Assertive };

        var gentlePlan = new TidyEngine().CreatePlan([Visible(window)], gentle);
        var assertiveTarget = TargetFor(new TidyEngine().CreatePlan([Visible(window)], assertive), window);

        Assert.Empty(gentlePlan);
        Assert.Equal(0, assertiveTarget.Left);
        Assert.Equal(window.VisualRect.Width, assertiveTarget.Width);
    }

    [Fact]
    public void SmartMode_ForegroundWindowMovesLessWhenClosingGap()
    {
        var foreground = Window(1, new RectI(300, 200, 500, 500), 0, foreground: true);
        var peer = Window(2, new RectI(830, 200, 500, 500), 1);

        var plan = new TidyEngine().CreatePlan([Visible(foreground), Visible(peer)]);
        var foregroundTarget = TargetFor(plan, foreground);
        var peerTarget = TargetFor(plan, peer);

        Assert.InRange(Math.Abs(foregroundTarget.Right - peerTarget.Left), 0, 1);
        Assert.True(
            Math.Abs(foregroundTarget.X - foreground.VisualRect.X) <
            Math.Abs(peerTarget.X - peer.VisualRect.X));
    }

    [Fact]
    public void SmartMode_DoesNotCreateCascadingRelationsAfterFirstMove()
    {
        var left = Window(1, new RectI(200, 200, 500, 500), 0);
        var middle = Window(2, new RectI(670, 200, 500, 500), 1);
        var right = Window(3, new RectI(1220, 200, 500, 500), 2);

        var plan = new TidyEngine().CreatePlan(
            [Visible(left), Visible(middle), Visible(right)],
            new TidyOptions(RescueOffscreenWindows: false));
        var middleTarget = TargetFor(plan, middle);
        var rightTarget = TargetFor(plan, right);

        // Left/middle begin with a 30 px overlap, so resolving them pushes middle right. That makes
        // the original 50 px middle/right gap become < 40 px. Because relationships are inferred
        // once from the original layout, right must not suddenly join the component on a later pass.
        Assert.NotEqual(middle.VisualRect, middleTarget);
        Assert.Equal(right.VisualRect, rightTarget);
        Assert.True(rightTarget.Left - middleTarget.Right > 0);
    }

    [Fact]
    public void SmartMode_PreservesThreeWindowTopologyWithoutRetiling()
    {
        var top = Window(1, new RectI(210, 120, 1480, 430), 0, foreground: true);
        var bottomLeft = Window(2, new RectI(214, 576, 710, 390), 1);
        var bottomRight = Window(3, new RectI(950, 574, 740, 392), 2);
        var options = new TidyOptions(
            SmartStrength: SmartTidyStrength.Assertive,
            RescueOffscreenWindows: true);

        var plan = new TidyEngine().CreatePlan(
            [Visible(top), Visible(bottomLeft), Visible(bottomRight)],
            options);
        var topTarget = TargetFor(plan, top);
        var leftTarget = TargetFor(plan, bottomLeft);
        var rightTarget = TargetFor(plan, bottomRight);

        Assert.Equal(top.VisualRect.Width, topTarget.Width);
        Assert.Equal(top.VisualRect.Height, topTarget.Height);
        Assert.Equal(bottomLeft.VisualRect.Width, leftTarget.Width);
        Assert.Equal(bottomRight.VisualRect.Width, rightTarget.Width);
        Assert.True(leftTarget.Left < rightTarget.Left);
        Assert.True(topTarget.Top < leftTarget.Top);
        Assert.InRange(Math.Abs(leftTarget.Right - rightTarget.Left), 0, 1);
        Assert.InRange(Math.Abs(topTarget.Bottom - leftTarget.Top), 0, 1);
        Assert.InRange(Math.Abs(topTarget.Bottom - rightTarget.Top), 0, 1);
    }

    [Fact]
    public void ClassicMode_RemainsAvailableAsDeterministicFallback()
    {
        var left = Window(1, new RectI(200, 200, 500, 500), 0);
        var right = Window(2, new RectI(740, 200, 500, 500), 1);
        var options = new TidyOptions(
            AlgorithmMode: TidyAlgorithmMode.Classic,
            NeighborSnapDistance: 50,
            AlignmentSnapDistance: 0,
            ScreenSnapDistance: 0,
            MaximumEdgeAdjustment: 200,
            RescueOffscreenWindows: false,
            Passes: 1);

        var plan = new TidyEngine().CreatePlan([Visible(left), Visible(right)], options);

        Assert.Equal(TargetFor(plan, left).Right, TargetFor(plan, right).Left);
    }

    private static WindowSnapshot Window(
        int id,
        RectI rect,
        int zOrder,
        bool foreground = false,
        bool manageable = true,
        bool resizable = true) =>
        new(
            (nint)id,
            rect,
            rect,
            WorkArea,
            (nint)1,
            new FrameInsets(0, 0, 0, 0),
            resizable,
            foreground,
            manageable,
            zOrder);

    private static VisibleWindow Visible(WindowSnapshot window) =>
        new(window, window.VisualRect.Area, window.VisualRect.Area, 1.0);

    private static RectI TargetFor(IReadOnlyList<TidyMove> plan, WindowSnapshot window) =>
        plan.FirstOrDefault(move => move.Window.Handle == window.Handle)?.TargetVisualRect ?? window.VisualRect;
}

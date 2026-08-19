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
    public void TidyEngine_ClosesSmallHorizontalGap()
    {
        var left = Window(1, new RectI(0, 0, 900, 1080), 0);
        var right = Window(2, new RectI(924, 0, 996, 1080), 1);

        var plan = new TidyEngine().CreatePlan([Visible(left), Visible(right)]);
        var leftTarget = TargetFor(plan, left);
        var rightTarget = TargetFor(plan, right);

        Assert.Equal(leftTarget.Right, rightTarget.Left);
        Assert.Equal(0, leftTarget.Left);
        Assert.Equal(1920, rightTarget.Right);
    }

    [Fact]
    public void TidyEngine_RemovesSmallHorizontalOverlap()
    {
        var left = Window(1, new RectI(0, 0, 950, 1080), 0);
        var right = Window(2, new RectI(930, 0, 990, 1080), 1);

        var plan = new TidyEngine().CreatePlan([Visible(left), Visible(right)]);
        var leftTarget = TargetFor(plan, left);
        var rightTarget = TargetFor(plan, right);

        Assert.Equal(leftTarget.Right, rightTarget.Left);
    }

    [Fact]
    public void TidyEngine_UsesNearbyScreenEdgesWithoutChangingLayoutModel()
    {
        var window = Window(1, new RectI(40, 40, 1840, 1000), 0);

        var plan = new TidyEngine().CreatePlan([Visible(window)]);
        var target = TargetFor(plan, window);

        Assert.Equal(WorkArea, target);
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
    public void TidyEngine_RescuesWindowFarOutsideWorkAreaBeyondNormalBudget()
    {
        var window = Window(1, new RectI(-420, 180, 700, 500), 0);
        var options = new TidyOptions(MaximumEdgeAdjustment: 32);

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
        var options = new TidyOptions(
            NeighborSnapDistance: 0,
            AlignmentSnapDistance: 0,
            ScreenSnapDistance: 0,
            MaximumEdgeAdjustment: 0,
            RescueOffscreenWindows: false,
            Passes: 1);

        var plan = new TidyEngine().CreatePlan([Visible(window)], options);

        Assert.Empty(plan);
    }

    [Fact]
    public void TidyEngine_RespectsCustomNeighborSnapDistance()
    {
        var left = Window(1, new RectI(200, 200, 500, 500), 0);
        var right = Window(2, new RectI(740, 200, 500, 500), 1);
        var conservative = new TidyOptions(
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

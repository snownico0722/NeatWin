using NeatWin.Core;

namespace NeatWin.Tests;

public sealed class SmartBehaviorTests
{
    private static readonly RectI WorkArea = new(0, 0, 1920, 1080);

    [Fact]
    public void VerticalFillPreference_FillsOnlyClearlyNearlyFullHeightWindow()
    {
        var window = Window(1, new RectI(300, 20, 700, 1040), 0);
        var plan = SmartPlanPostProcessor.Refine(
            [Visible(window)],
            [],
            new TidyOptions(RescueOffscreenWindows: false),
            new SmartBehaviorOptions(PreferReversibleVerticalFill: true));

        var target = TargetFor(plan, window);

        Assert.Equal(WorkArea.Top, target.Top);
        Assert.Equal(WorkArea.Bottom, target.Bottom);
        Assert.Equal(window.VisualRect.Left, target.Left);
        Assert.Equal(window.VisualRect.Right, target.Right);
    }

    [Fact]
    public void VerticalFillPreference_DoesNotPromoteMerelyTallWindow()
    {
        var window = Window(1, new RectI(300, 40, 700, 1000), 0);
        var plan = SmartPlanPostProcessor.Refine(
            [Visible(window)],
            [],
            new TidyOptions(RescueOffscreenWindows: false),
            new SmartBehaviorOptions(PreferReversibleVerticalFill: true));

        Assert.Empty(plan);
    }

    [Fact]
    public void VerticalFillPreference_CanBeDisabled()
    {
        var window = Window(1, new RectI(300, 20, 700, 1040), 0);
        var plan = SmartPlanPostProcessor.Refine(
            [Visible(window)],
            [],
            new TidyOptions(RescueOffscreenWindows: false),
            new SmartBehaviorOptions(PreferReversibleVerticalFill: false));

        Assert.Empty(plan);
    }

    [Fact]
    public void BalancedOverlapAvoidance_SeparatesModerateAccidentalOverlap()
    {
        var left = Window(1, new RectI(200, 200, 600, 500), 0);
        var right = Window(2, new RectI(680, 200, 600, 500), 1);

        var plan = SmartPlanPostProcessor.Refine(
            [Visible(left), Visible(right)],
            [],
            new TidyOptions(RescueOffscreenWindows: false),
            new SmartBehaviorOptions(OverlapAvoidance: SmartOverlapAvoidance.Balanced));

        var leftTarget = TargetFor(plan, left);
        var rightTarget = TargetFor(plan, right);

        Assert.Equal(0, OverlapArea(leftTarget, rightTarget));
    }

    [Fact]
    public void BalancedOverlapAvoidance_LeavesDeepLikelyIntentionalOverlapAlone()
    {
        var left = Window(1, new RectI(200, 200, 600, 500), 0);
        var right = Window(2, new RectI(500, 200, 600, 500), 1);

        var plan = SmartPlanPostProcessor.Refine(
            [Visible(left), Visible(right)],
            [],
            new TidyOptions(RescueOffscreenWindows: false),
            new SmartBehaviorOptions(OverlapAvoidance: SmartOverlapAvoidance.Balanced));

        Assert.Empty(plan);
    }

    [Fact]
    public void StrongOverlapAvoidance_HandlesDeeperOverlapThanGentle()
    {
        var left = Window(1, new RectI(200, 200, 600, 500), 0);
        var right = Window(2, new RectI(500, 200, 600, 500), 1);
        var tidyOptions = new TidyOptions(RescueOffscreenWindows: false);

        var gentlePlan = SmartPlanPostProcessor.Refine(
            [Visible(left), Visible(right)],
            [],
            tidyOptions,
            new SmartBehaviorOptions(OverlapAvoidance: SmartOverlapAvoidance.Gentle));
        var strongPlan = SmartPlanPostProcessor.Refine(
            [Visible(left), Visible(right)],
            [],
            tidyOptions,
            new SmartBehaviorOptions(OverlapAvoidance: SmartOverlapAvoidance.Strong));

        var gentleOverlap = OverlapArea(TargetFor(gentlePlan, left), TargetFor(gentlePlan, right));
        var strongOverlap = OverlapArea(TargetFor(strongPlan, left), TargetFor(strongPlan, right));

        Assert.True(strongOverlap < gentleOverlap);
        Assert.Equal(0, strongOverlap);
    }

    [Fact]
    public void BalancedOverlapAvoidance_PrefersVerticalSeparationForSharedRightColumn()
    {
        var a = Window(1, new RectI(100, 100, 700, 800), 0);
        var b = Window(2, new RectI(900, 200, 700, 400), 1);
        var c = Window(3, new RectI(1340, 400, 580, 400), 2);

        var plan = SmartPlanPostProcessor.Refine(
            [Visible(a), Visible(b), Visible(c)],
            [],
            new TidyOptions(RescueOffscreenWindows: true),
            new SmartBehaviorOptions(OverlapAvoidance: SmartOverlapAvoidance.Balanced));

        var aTarget = TargetFor(plan, a);
        var bTarget = TargetFor(plan, b);
        var cTarget = TargetFor(plan, c);

        Assert.Equal(a.VisualRect, aTarget);
        Assert.Equal(b.VisualRect.Left, bTarget.Left);
        Assert.Equal(c.VisualRect.Left, cTarget.Left);
        Assert.True(bTarget.Top < b.VisualRect.Top);
        Assert.True(cTarget.Top > c.VisualRect.Top);
        Assert.Equal(0, OverlapArea(bTarget, cTarget));
    }

    [Fact]
    public void OverlapAvoidance_ReallocatesBlockedMovementToWindowWithFreeSpace()
    {
        var top = Window(1, new RectI(1000, 0, 700, 500), 0);
        var bottom = Window(2, new RectI(1000, 410, 700, 500), 1);

        var plan = SmartPlanPostProcessor.Refine(
            [Visible(top), Visible(bottom)],
            [],
            new TidyOptions(RescueOffscreenWindows: true),
            new SmartBehaviorOptions(OverlapAvoidance: SmartOverlapAvoidance.Balanced));

        var topTarget = TargetFor(plan, top);
        var bottomTarget = TargetFor(plan, bottom);

        Assert.Equal(0, topTarget.Top);
        Assert.True(bottomTarget.Top >= 501);
        Assert.Equal(0, OverlapArea(topTarget, bottomTarget));
    }

    [Fact]
    public void VideoBlackBars_ShrinkTendency_ReducesBarDimensionOnly()
    {
        var browser = Window(1, new RectI(100, 100, 1200, 800), 0);
        var hint = new VideoBlackBarHint(
            browser.Handle,
            VideoBlackBarOrientation.Horizontal,
            2.0,
            new RectI(150, 190, 1100, 620),
            0.95);

        var plan = VideoAspectPostProcessor.Refine(
            [Visible(browser)],
            [],
            new TidyOptions(),
            new SmartBehaviorOptions(
                RemoveVideoBlackBars: true,
                VideoBlackBarTendency: VideoBlackBarTendency.Shrink),
            hint);

        var target = TargetFor(plan, browser);
        Assert.Equal(browser.VisualRect.Width, target.Width);
        Assert.Equal(730, target.Height);
    }

    [Fact]
    public void VideoBlackBars_ExpandTendency_GrowsOnlyEnoughToReachAspect()
    {
        var browser = Window(1, new RectI(100, 100, 1200, 800), 0);
        var hint = new VideoBlackBarHint(
            browser.Handle,
            VideoBlackBarOrientation.Horizontal,
            2.0,
            new RectI(150, 190, 1100, 620),
            0.95);

        var plan = VideoAspectPostProcessor.Refine(
            [Visible(browser)],
            [],
            new TidyOptions(),
            new SmartBehaviorOptions(
                RemoveVideoBlackBars: true,
                VideoBlackBarTendency: VideoBlackBarTendency.Expand),
            hint);

        var target = TargetFor(plan, browser);
        Assert.Equal(1340, target.Width);
        Assert.Equal(browser.VisualRect.Height, target.Height);
    }

    [Fact]
    public void VideoBlackBars_OverridesGenericFullHeightPlanInsteadOfExpandingFromIt()
    {
        var browser = Window(1, new RectI(100, 100, 1200, 800), 0);
        var genericFullHeightPlan = new[]
        {
            new TidyMove(browser, new RectI(100, 0, 1200, 1080)),
        };
        var hint = new VideoBlackBarHint(
            browser.Handle,
            VideoBlackBarOrientation.Horizontal,
            2.0,
            new RectI(150, 190, 1100, 620),
            0.95);

        var plan = VideoAspectPostProcessor.Refine(
            [Visible(browser)],
            genericFullHeightPlan,
            new TidyOptions(),
            new SmartBehaviorOptions(
                RemoveVideoBlackBars: true,
                VideoBlackBarTendency: VideoBlackBarTendency.Expand),
            hint);

        var target = TargetFor(plan, browser);
        Assert.Equal(1340, target.Width);
        Assert.Equal(800, target.Height);
        Assert.NotEqual(WorkArea.Height, target.Height);
    }

    [Fact]
    public void VideoBlackBars_ExpandTendency_FallsBackToShrinkWhenGrowthWouldOverlapNeighbor()
    {
        var browser = Window(1, new RectI(100, 100, 1200, 800), 0);
        var neighbor = Window(2, new RectI(1320, 100, 500, 800), 1);
        var hint = new VideoBlackBarHint(
            browser.Handle,
            VideoBlackBarOrientation.Horizontal,
            2.0,
            new RectI(150, 190, 1100, 620),
            0.95);

        var plan = VideoAspectPostProcessor.Refine(
            [Visible(browser), Visible(neighbor)],
            [],
            new TidyOptions(),
            new SmartBehaviorOptions(
                RemoveVideoBlackBars: true,
                VideoBlackBarTendency: VideoBlackBarTendency.Expand),
            hint);

        var target = TargetFor(plan, browser);
        Assert.Equal(browser.VisualRect.Width, target.Width);
        Assert.Equal(730, target.Height);
        Assert.Equal(0, OverlapArea(target, neighbor.VisualRect));
    }

    private static long OverlapArea(RectI a, RectI b) => a.Intersect(b).Area;

    private static WindowSnapshot Window(int id, RectI rect, int zOrder) =>
        new(
            (nint)id,
            rect,
            rect,
            WorkArea,
            (nint)1,
            new FrameInsets(0, 0, 0, 0),
            IsResizable: true,
            IsForeground: zOrder == 0,
            IsManageable: true,
            ZOrder: zOrder);

    private static VisibleWindow Visible(WindowSnapshot window) =>
        new(window, window.VisualRect.Area, window.VisualRect.Area, 1.0);

    private static RectI TargetFor(IReadOnlyList<TidyMove> plan, WindowSnapshot window) =>
        plan.FirstOrDefault(move => move.Window.Handle == window.Handle)?.TargetVisualRect ?? window.VisualRect;
}

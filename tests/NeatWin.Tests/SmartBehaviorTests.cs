using NeatWin.Core;

namespace NeatWin.Tests;

public sealed class SmartBehaviorTests
{
    private static readonly RectI WorkArea = new(0, 0, 1920, 1080);

    [Fact]
    public void VerticalFillPreference_FillsNearlyFullHeightWindow()
    {
        var window = Window(1, new RectI(300, 40, 700, 1000), 0);
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
    public void VerticalFillPreference_CanBeDisabled()
    {
        var window = Window(1, new RectI(300, 40, 700, 1000), 0);
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

        Assert.True(OverlapArea(leftTarget, rightTarget) == 0);
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

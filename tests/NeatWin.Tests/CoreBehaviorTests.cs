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
    public void TidyOptions_DefaultToSmartMode()
    {
        Assert.Equal(TidyAlgorithmMode.Smart, new TidyOptions().AlgorithmMode);
    }

    [Fact]
    public void TidyEngine_ClosesSmallHorizontalGap()
    {
        var left = Window(1, new RectI(0, 0, 900, 1080), 0);
        var right = Window(2, new RectI(924, 0, 996, 1080), 1);

        var plan = new TidyEngine().CreatePlan([Visible(left), Visible(right)]);
        var leftTarget = TargetFor(plan, left);
        var rightTarget = TargetFor(plan, right);

        Assert.InRange(Math.Abs(leftTarget.Right - rightTarget.Left), 0, 1);
        Assert.InRange(Math.Abs(leftTarget.Left), 0, 1);
        Assert.InRange(Math.Abs(1920 - rightTarget.Right), 0, 1);
    }

    [Fact]
    public void TidyEngine_RemovesSmallHorizontalOverlap()
    {
        var left = Window(1, new RectI(0, 0, 950, 1080), 0);
        var right = Window(2, new RectI(930, 0, 990, 1080), 1);

        var plan = new TidyEngine().CreatePlan([Visible(left), Visible(right)]);
        var leftTarget = TargetFor(plan, left);
        var rightTarget = TargetFor(plan, right);

        Assert.InRange(Math.Abs(leftTarget.Right - rightTarget.Left), 0, 1);
    }

    [Fact]
    public void TidyEngine_UsesNearbyScreenEdgesWithoutChangingLayoutModel()
    {
        var window = Window(1, new RectI(40, 40, 1840, 1000), 0);

        var plan = new TidyEngine().CreatePlan([Visible(window)]);
        var target = TargetFor(plan, window);

        Assert.InRange(Math.Abs(target.Left - WorkArea.Left), 0, 1);
        Assert.InRange(Math.Abs(target.Top - WorkArea.Top), 0, 1);
        Assert.InRange(Math.Abs(target.Right - WorkArea.Right), 0, 1);
        Assert.InRange(Math.Abs(target.Bottom - WorkArea.Bottom), 0, 1);
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
            ResizeResistanceWeight: 0,
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
        Assert.InRange(
            Math.Abs(TargetFor(generousPlan, left).Right - TargetFor(generousPlan, right).Left),
            0,
            1);
    }

    [Fact]
    public void SmartMode_OrderlinessWeightControlsHowStronglyRelationsConverge()
    {
        var left = Window(1, new RectI(200, 200, 500, 500), 0);
        var right = Window(2, new RectI(748, 200, 500, 500), 1);
        var baseOptions = new TidyOptions(
            AlgorithmMode: TidyAlgorithmMode.Smart,
            PreserveLayoutWeight: 2.0,
            OrderlinessWeight: 0.15,
            SpaceUsageWeight: 0,
            SmartIterations: 48,
            NeighborSnapDistance: 60,
            AlignmentSnapDistance: 0,
            ScreenSnapDistance: 0,
            MaximumEdgeAdjustment: 100,
            RescueOffscreenWindows: false);

        var weakPlan = new TidyEngine().CreatePlan([Visible(left), Visible(right)], baseOptions);
        var strongPlan = new TidyEngine().CreatePlan(
            [Visible(left), Visible(right)],
            baseOptions with { OrderlinessWeight = 4.0 });

        var weakGap = Math.Abs(TargetFor(weakPlan, right).Left - TargetFor(weakPlan, left).Right);
        var strongGap = Math.Abs(TargetFor(strongPlan, right).Left - TargetFor(strongPlan, left).Right);

        Assert.True(strongGap < weakGap);
    }

    [Fact]
    public void SmartMode_ResizeResistancePrefersTranslationOverChangingWidth()
    {
        var window = Window(1, new RectI(40, 220, 600, 500), 0);
        var flexibleOptions = new TidyOptions(
            AlgorithmMode: TidyAlgorithmMode.Smart,
            PreserveLayoutWeight: 0.5,
            ResizeResistanceWeight: 0,
            OrderlinessWeight: 1,
            SpaceUsageWeight: 3,
            SmartIterations: 64,
            NeighborSnapDistance: 0,
            AlignmentSnapDistance: 0,
            ScreenSnapDistance: 80,
            MaximumEdgeAdjustment: 100,
            MaximumSizeChangeRatio: 0.20,
            RescueOffscreenWindows: false);
        var resistantOptions = flexibleOptions with { ResizeResistanceWeight = 5.0 };

        var flexibleTarget = TargetFor(
            new TidyEngine().CreatePlan([Visible(window)], flexibleOptions),
            window);
        var resistantTarget = TargetFor(
            new TidyEngine().CreatePlan([Visible(window)], resistantOptions),
            window);

        var flexibleWidthError = Math.Abs(flexibleTarget.Width - window.VisualRect.Width);
        var resistantWidthError = Math.Abs(resistantTarget.Width - window.VisualRect.Width);

        Assert.True(resistantWidthError < flexibleWidthError);
        Assert.True(resistantTarget.Left < window.VisualRect.Left);
    }

    [Fact]
    public void SmartMode_ConvergesThreeWindowTopologyWithoutRetiling()
    {
        var top = Window(1, new RectI(18, 16, 1880, 500), 0, foreground: true);
        var bottomLeft = Window(2, new RectI(24, 535, 900, 526), 1);
        var bottomRight = Window(3, new RectI(945, 531, 950, 532), 2);
        var options = new TidyOptions(
            AlgorithmMode: TidyAlgorithmMode.Smart,
            PreserveLayoutWeight: 0.8,
            ResizeResistanceWeight: 0.8,
            OrderlinessWeight: 3.0,
            SpaceUsageWeight: 2.0,
            SmartIterations: 72,
            NeighborSnapDistance: 64,
            AlignmentSnapDistance: 30,
            ScreenSnapDistance: 80,
            MaximumEdgeAdjustment: 120,
            MaximumSizeChangeRatio: 0.20,
            RescueOffscreenWindows: true);

        var plan = new TidyEngine().CreatePlan(
            [Visible(top), Visible(bottomLeft), Visible(bottomRight)],
            options);
        var topTarget = TargetFor(plan, top);
        var leftTarget = TargetFor(plan, bottomLeft);
        var rightTarget = TargetFor(plan, bottomRight);

        Assert.InRange(Math.Abs(topTarget.Left - WorkArea.Left), 0, 2);
        Assert.InRange(Math.Abs(topTarget.Right - WorkArea.Right), 0, 2);
        Assert.InRange(Math.Abs(leftTarget.Left - WorkArea.Left), 0, 2);
        Assert.InRange(Math.Abs(rightTarget.Right - WorkArea.Right), 0, 2);
        Assert.InRange(Math.Abs(leftTarget.Bottom - WorkArea.Bottom), 0, 2);
        Assert.InRange(Math.Abs(rightTarget.Bottom - WorkArea.Bottom), 0, 2);
        Assert.InRange(Math.Abs(leftTarget.Right - rightTarget.Left), 0, 2);
        Assert.InRange(Math.Abs(topTarget.Bottom - leftTarget.Top), 0, 2);
        Assert.InRange(Math.Abs(topTarget.Bottom - rightTarget.Top), 0, 2);
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

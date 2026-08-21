using NeatWin.Core;

namespace NeatWin.Tests;

public sealed class HumanCenteredSmartTests
{
    private static readonly RectI WorkArea = new(0, 0, 1920, 1080);

    [Fact]
    public void AutoLayout_LowEvidenceFloatingWindowsAreNotRetiled()
    {
        var left = Window(1, new RectI(180, 190, 520, 500), 0, foreground: true);
        var right = Window(2, new RectI(1160, 260, 500, 480), 1);
        var visible = new[] { Visible(left), Visible(right) };

        var result = HumanCenteredSmartPlanner.CreatePlan(
            visible,
            new TidyOptions(SmartStrength: SmartTidyStrength.Balanced, RescueOffscreenWindows: false),
            SmartInteractionContext.Empty,
            SmartPersonalizationState.Default);

        Assert.Empty(result.Moves);
        Assert.NotNull(result.Learning);
        Assert.Equal(AutoLayoutArchetype.Preserve, result.Learning!.Archetype);
    }

    [Fact]
    public void AutoLayout_CleaningSmallHumanPlacementErrorsDoesNotRequireFullRetile()
    {
        var left = Window(1, new RectI(12, 14, 928, 1052), 0, foreground: true);
        var right = Window(2, new RectI(950, 10, 958, 1058), 1);
        var visible = new[] { Visible(left), Visible(right) };

        var result = HumanCenteredSmartPlanner.CreatePlan(
            visible,
            new TidyOptions(SmartStrength: SmartTidyStrength.Balanced, RescueOffscreenWindows: true),
            new SmartInteractionContext(
                left.Handle,
                new PointI(500, 400),
                [new WindowAttentionSignal(left.Handle, 1.4, 1, 0.5, 0.7, 1)]),
            SmartPersonalizationState.Default);

        var leftTarget = TargetFor(result.Moves, left);
        var rightTarget = TargetFor(result.Moves, right);

        Assert.Equal(WorkArea, leftTarget.Intersect(WorkArea).Intersect(leftTarget));
        Assert.True(leftTarget.Width >= 700);
        Assert.True(rightTarget.Width >= 700);
        Assert.True(leftTarget.Intersect(rightTarget).Area == 0);
        Assert.True(Math.Abs(leftTarget.Width - left.VisualRect.Width) < 180);
        Assert.True(Math.Abs(rightTarget.Width - right.VisualRect.Width) < 180);
    }

    [Fact]
    public void AutoPersonalization_PairwiseCorrectionMovesBiasTowardCorrectedArchetype()
    {
        var chosen = new SmartLearningSnapshot(
            AutoLayoutArchetype.EqualColumns,
            Enumerable.Repeat(0.0, SmartPersonalizationState.AutoFeatureCount).ToArray(),
            new Dictionary<nint, RectI>());
        var correctedFeatures = Enumerable.Repeat(0.0, SmartPersonalizationState.AutoFeatureCount).ToArray();
        correctedFeatures[6] = 0.8;
        correctedFeatures[7] = 0.9;

        var learned = SmartPersonalizationLearner.LearnAutoCorrection(
            SmartPersonalizationState.Default,
            chosen,
            AutoLayoutArchetype.FocusLeft,
            correctedFeatures,
            confidence: 1.0);

        Assert.True(learned.AutoArchetypeBias[(int)AutoLayoutArchetype.FocusLeft] > 0);
        Assert.True(learned.AutoArchetypeBias[(int)AutoLayoutArchetype.EqualColumns] < 0);
        Assert.True(learned.AutoFeatureWeights[6] > 0);
        Assert.True(learned.AutoFeatureWeights[7] > 0);
        Assert.Equal(1, learned.AutoCorrectionSamples);
    }

    [Fact]
    public void FollowHand_SlowReleaseNearScreenEdgeFinishesTheLastPixels()
    {
        var moved = Window(1, new RectI(13, 180, 600, 520), 0, foreground: true);
        var gesture = Gesture(
            moved.Handle,
            new RectI(240, 180, 600, 520),
            moved.VisualRect,
            ManualGestureKind.Move,
            endSpeed: 45,
            [
                new PointerMotionSample(new PointI(300, 240), 0),
                new PointerMotionSample(new PointI(95, 240), 240),
                new PointerMotionSample(new PointI(28, 240), 400),
                new PointerMotionSample(new PointI(18, 240), 500),
            ]);

        var decision = FollowHandAssistant.Decide(
            gesture,
            [Visible(moved)],
            new TidyOptions(SmartStrength: SmartTidyStrength.Balanced),
            SmartInteractionContext.Empty,
            SmartPersonalizationState.Default);

        Assert.True(decision.ShouldApply);
        Assert.Equal(FollowHandTargetKind.ScreenEdge, decision.TargetKind);
        Assert.Equal(0, decision.TargetRect.Left);
        Assert.Equal(moved.VisualRect.Size(), decision.TargetRect.Size());
    }

    [Fact]
    public void FollowHand_FastFlyThroughDoesNotMagnetizeToScreenEdge()
    {
        var moved = Window(1, new RectI(13, 180, 600, 520), 0, foreground: true);
        var gesture = Gesture(
            moved.Handle,
            new RectI(900, 180, 600, 520),
            moved.VisualRect,
            ManualGestureKind.Move,
            endSpeed: 6500,
            [
                new PointerMotionSample(new PointI(1200, 240), 0),
                new PointerMotionSample(new PointI(600, 240), 35),
                new PointerMotionSample(new PointI(30, 240), 70),
            ]);

        var decision = FollowHandAssistant.Decide(
            gesture,
            [Visible(moved)],
            new TidyOptions(SmartStrength: SmartTidyStrength.Balanced),
            SmartInteractionContext.Empty,
            SmartPersonalizationState.Default);

        Assert.False(decision.ShouldApply);
        Assert.Equal(moved.VisualRect, decision.TargetRect);
    }

    [Fact]
    public void FollowHand_LearnsPreferredGapFromRawMouseUpNotAssistedTarget()
    {
        var moved = Window(1, new RectI(186, 200, 600, 500), 0, foreground: true);
        var neighbor = Window(2, new RectI(800, 200, 600, 500), 1);
        var gesture = Gesture(
            moved.Handle,
            new RectI(130, 200, 600, 500),
            moved.VisualRect,
            ManualGestureKind.Move,
            endSpeed: 80,
            []);

        var learned = FollowHandAssistant.LearnFromRawGesture(
            SmartPersonalizationState.Default,
            gesture,
            [Visible(moved), Visible(neighbor)]);

        Assert.True(learned.PreferredGapPixels > SmartPersonalizationState.Default.PreferredGapPixels);
        Assert.True(learned.PreferredGapPixels < 14.1);
        Assert.True(learned.FollowTargetBias[(int)FollowHandTargetKind.NeighborGap] > 0);
        Assert.Equal(1, learned.FollowGestureSamples);
    }

    [Fact]
    public void FollowHand_PersonalGapChangesThePredictedEndpoint()
    {
        var moved = Window(1, new RectI(174, 200, 600, 500), 0, foreground: true);
        var neighbor = Window(2, new RectI(800, 200, 600, 500), 1);
        var gesture = Gesture(
            moved.Handle,
            new RectI(130, 200, 600, 500),
            moved.VisualRect,
            ManualGestureKind.Move,
            endSpeed: 60,
            [
                new PointerMotionSample(new PointI(300, 250), 0),
                new PointerMotionSample(new PointI(230, 250), 180),
                new PointerMotionSample(new PointI(180, 250), 360),
            ]);
        var personal = SmartPersonalizationState.Default with
        {
            PreferredGapPixels = 20,
            FollowGestureSamples = 80,
            FollowTargetBias = Bias(FollowHandTargetKind.NeighborGap, 0.8),
        };

        var decision = FollowHandAssistant.Decide(
            gesture,
            [Visible(moved), Visible(neighbor)],
            new TidyOptions(SmartStrength: SmartTidyStrength.Balanced),
            SmartInteractionContext.Empty,
            personal);

        Assert.True(decision.ShouldApply);
        Assert.Equal(FollowHandTargetKind.NeighborGap, decision.TargetKind);
        Assert.Equal(20, neighbor.VisualRect.Left - decision.TargetRect.Right);
    }

    [Fact]
    public void ExplicitBehavior_BalancedVerticalFillRequiresGenuineNearFullHeightIntent()
    {
        var notCloseEnough = Window(1, new RectI(300, 70, 700, 940), 0, foreground: true);
        var almostFull = Window(2, new RectI(300, 22, 700, 1036), 0, foreground: true);
        var options = new TidyOptions(SmartStrength: SmartTidyStrength.Balanced, RescueOffscreenWindows: false);
        var behavior = new SmartBehaviorOptions(PreferReversibleVerticalFill: true);

        var first = HumanCenteredExplicitBehaviors.Refine([Visible(notCloseEnough)], [], options, behavior);
        var second = HumanCenteredExplicitBehaviors.Refine([Visible(almostFull)], [], options, behavior);

        Assert.Empty(first);
        Assert.Equal(WorkArea.Top, TargetFor(second, almostFull).Top);
        Assert.Equal(WorkArea.Bottom, TargetFor(second, almostFull).Bottom);
    }

    [Fact]
    public void ExplicitBehavior_DeepOriginalOverlapIsTreatedAsIntentionalStacking()
    {
        var a = Window(1, new RectI(250, 180, 850, 650), 0, foreground: true);
        var b = Window(2, new RectI(520, 260, 850, 650), 1);

        var result = HumanCenteredExplicitBehaviors.Refine(
            [Visible(a), Visible(b)],
            [],
            new TidyOptions(SmartStrength: SmartTidyStrength.Balanced, RescueOffscreenWindows: false),
            new SmartBehaviorOptions(PreferReversibleVerticalFill: false));

        Assert.Empty(result);
    }

    private static ManualWindowGesture Gesture(
        nint handle,
        RectI start,
        RectI end,
        ManualGestureKind kind,
        double endSpeed,
        IReadOnlyList<PointerMotionSample> samples) =>
        new(handle, start, end, WorkArea, kind, samples, 500, endSpeed);

    private static double[] Bias(FollowHandTargetKind kind, double value)
    {
        var result = new double[SmartPersonalizationState.FollowTargetCount];
        result[(int)kind] = value;
        return result;
    }

    private static WindowSnapshot Window(
        int id,
        RectI rect,
        int zOrder,
        bool foreground = false) =>
        new(
            (nint)id,
            rect,
            rect,
            WorkArea,
            (nint)1,
            new FrameInsets(0, 0, 0, 0),
            IsResizable: true,
            IsForeground: foreground,
            IsManageable: true,
            ZOrder: zOrder);

    private static VisibleWindow Visible(WindowSnapshot window) =>
        new(window, window.VisualRect.Area, window.VisualRect.Area, 1.0);

    private static RectI TargetFor(IReadOnlyList<TidyMove> plan, WindowSnapshot window) =>
        plan.FirstOrDefault(move => move.Window.Handle == window.Handle)?.TargetVisualRect ?? window.VisualRect;
}

internal static class RectITestExtensions
{
    internal static Size Size(this RectI rect) => new(rect.Width, rect.Height);
}

using NeatWin.Core;

namespace NeatWin.Tests;

public sealed class FollowHandDiagnosticsTests
{
    [Fact]
    public void LearnedNeighborGap_HasEnoughEvidenceToAssist()
    {
        var work = new RectI(0, 0, 1920, 1080);
        var moved = Window(1, new RectI(174, 200, 600, 500), 0, work, true);
        var neighbor = Window(2, new RectI(800, 200, 600, 500), 1, work, false);
        var gesture = new ManualWindowGesture(
            moved.Handle,
            new RectI(130, 200, 600, 500),
            moved.VisualRect,
            work,
            ManualGestureKind.Move,
            [
                new PointerMotionSample(new PointI(300, 250), 0),
                new PointerMotionSample(new PointI(230, 250), 180),
                new PointerMotionSample(new PointI(180, 250), 360),
            ],
            500,
            60);
        var bias = new double[SmartPersonalizationState.FollowTargetCount];
        bias[(int)FollowHandTargetKind.NeighborGap] = 0.8;
        var personal = SmartPersonalizationState.Default with
        {
            PreferredGapPixels = 20,
            FollowGestureSamples = 80,
            FollowTargetBias = bias,
        };

        var decision = FollowHandAssistant.Decide(
            gesture,
            [Visible(moved), Visible(neighbor)],
            new TidyOptions(SmartStrength: SmartTidyStrength.Balanced),
            SmartInteractionContext.Empty,
            personal);

        Assert.True(
            decision.ShouldApply,
            $"confidence={decision.Confidence:F3}; kind={decision.TargetKind}; target={decision.TargetRect}; raw={gesture.EndRect}");
    }

    private static WindowSnapshot Window(int id, RectI rect, int z, RectI work, bool foreground) =>
        new((nint)id, rect, rect, work, (nint)1, new FrameInsets(), true, foreground, true, z);

    private static VisibleWindow Visible(WindowSnapshot window) =>
        new(window, window.VisualRect.Area, window.VisualRect.Area, 1.0);
}

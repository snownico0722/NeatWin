namespace NeatWin.Core;

public enum VideoBlackBarOrientation
{
    Horizontal,
    Vertical,
}

public sealed record VideoBlackBarHint(
    nint WindowHandle,
    VideoBlackBarOrientation Orientation,
    double ContentAspectRatio,
    RectI ViewportVisualRect,
    double Confidence);

/// <summary>
/// Applies a high-confidence video-content aspect hint to one browser window without touching the
/// browser/player itself. Shrink removes the dimension that contains the bars; Expand grows the
/// orthogonal dimension and falls back to a mixed resize if the monitor edge prevents full growth.
/// </summary>
public static class VideoAspectPostProcessor
{
    private const double MinimumConfidence = 0.82;
    private const double MaximumSingleAdjustmentRatio = 0.45;

    public static IReadOnlyList<TidyMove> Refine(
        IReadOnlyList<VisibleWindow> visibleWindows,
        IReadOnlyList<TidyMove> basePlan,
        TidyOptions tidyOptions,
        SmartBehaviorOptions behaviorOptions,
        VideoBlackBarHint? hint)
    {
        if (tidyOptions.AlgorithmMode != TidyAlgorithmMode.Smart ||
            !behaviorOptions.RemoveVideoBlackBars ||
            hint is null ||
            hint.Confidence < MinimumConfidence ||
            hint.ContentAspectRatio is < 0.55 or > 4.5)
        {
            return basePlan;
        }

        var visible = visibleWindows.FirstOrDefault(item => item.Window.Handle == hint.WindowHandle);
        if (visible is null || !visible.Window.IsResizable || !visible.Window.IsManageable)
        {
            return basePlan;
        }

        var snapshot = visible.Window;
        var original = snapshot.VisualRect;
        if (original.IsEmpty || hint.ViewportVisualRect.IsEmpty)
        {
            return basePlan;
        }

        var current = basePlan
            .FirstOrDefault(move => move.Window.Handle == snapshot.Handle)?.TargetVisualRect ?? original;

        var chromeX = Math.Max(0, original.Width - hint.ViewportVisualRect.Width);
        var chromeY = Math.Max(0, original.Height - hint.ViewportVisualRect.Height);
        var viewportWidth = Math.Max(1, current.Width - chromeX);
        var viewportHeight = Math.Max(1, current.Height - chromeY);

        var targetSize = ComputeTargetSize(
            current,
            snapshot.WorkArea,
            viewportWidth,
            viewportHeight,
            chromeX,
            chromeY,
            hint.ContentAspectRatio,
            hint.Orientation,
            behaviorOptions.VideoBlackBarTendency,
            tidyOptions.MinimumWidth,
            tidyOptions.MinimumHeight);

        if (targetSize is null)
        {
            return basePlan;
        }

        var (targetWidth, targetHeight) = targetSize.Value;
        var widthDeltaRatio = Math.Abs(targetWidth - current.Width) / (double)Math.Max(1, current.Width);
        var heightDeltaRatio = Math.Abs(targetHeight - current.Height) / (double)Math.Max(1, current.Height);
        if (Math.Max(widthDeltaRatio, heightDeltaRatio) > MaximumSingleAdjustmentRatio)
        {
            return basePlan;
        }

        var target = ResizeAroundCenterAndFit(current, targetWidth, targetHeight, snapshot.WorkArea);
        if (!HasMeaningfulChange(current, target))
        {
            return basePlan;
        }

        var result = basePlan
            .Where(move => move.Window.Handle != snapshot.Handle)
            .ToList();
        result.Add(new TidyMove(snapshot, target));
        return result;
    }

    private static (int Width, int Height)? ComputeTargetSize(
        RectI current,
        RectI workArea,
        int viewportWidth,
        int viewportHeight,
        int chromeX,
        int chromeY,
        double contentAspect,
        VideoBlackBarOrientation orientation,
        VideoBlackBarTendency tendency,
        int minimumWidth,
        int minimumHeight)
    {
        double width = current.Width;
        double height = current.Height;

        if (orientation == VideoBlackBarOrientation.Horizontal)
        {
            if (tendency == VideoBlackBarTendency.Shrink)
            {
                height = (viewportWidth / contentAspect) + chromeY;
            }
            else
            {
                width = (viewportHeight * contentAspect) + chromeX;
                if (!workArea.IsEmpty && width > workArea.Width)
                {
                    width = workArea.Width;
                    var fittedViewportWidth = Math.Max(1, width - chromeX);
                    height = (fittedViewportWidth / contentAspect) + chromeY;
                }
            }
        }
        else
        {
            if (tendency == VideoBlackBarTendency.Shrink)
            {
                width = (viewportHeight * contentAspect) + chromeX;
            }
            else
            {
                height = (viewportWidth / contentAspect) + chromeY;
                if (!workArea.IsEmpty && height > workArea.Height)
                {
                    height = workArea.Height;
                    var fittedViewportHeight = Math.Max(1, height - chromeY);
                    width = (fittedViewportHeight * contentAspect) + chromeX;
                }
            }
        }

        var targetWidth = Math.Max(Math.Min(current.Width, minimumWidth), (int)Math.Round(width));
        var targetHeight = Math.Max(Math.Min(current.Height, minimumHeight), (int)Math.Round(height));

        if (!workArea.IsEmpty)
        {
            targetWidth = Math.Min(targetWidth, workArea.Width);
            targetHeight = Math.Min(targetHeight, workArea.Height);
        }

        var effectiveViewportWidth = Math.Max(1, targetWidth - chromeX);
        var effectiveViewportHeight = Math.Max(1, targetHeight - chromeY);
        var achievedAspect = effectiveViewportWidth / (double)effectiveViewportHeight;
        var aspectError = Math.Abs(achievedAspect - contentAspect) / contentAspect;
        return aspectError <= 0.035
            ? (targetWidth, targetHeight)
            : null;
    }

    private static RectI ResizeAroundCenterAndFit(RectI current, int width, int height, RectI workArea)
    {
        var centerX = (current.Left + current.Right) / 2.0;
        var centerY = (current.Top + current.Bottom) / 2.0;
        var left = (int)Math.Round(centerX - (width / 2.0));
        var top = (int)Math.Round(centerY - (height / 2.0));

        if (!workArea.IsEmpty)
        {
            if (width <= workArea.Width)
            {
                left = Math.Clamp(left, workArea.Left, workArea.Right - width);
            }
            if (height <= workArea.Height)
            {
                top = Math.Clamp(top, workArea.Top, workArea.Bottom - height);
            }
        }

        return new RectI(left, top, width, height);
    }

    private static bool HasMeaningfulChange(RectI a, RectI b) =>
        Math.Abs(a.Left - b.Left) >= 2 ||
        Math.Abs(a.Top - b.Top) >= 2 ||
        Math.Abs(a.Right - b.Right) >= 2 ||
        Math.Abs(a.Bottom - b.Bottom) >= 2;
}

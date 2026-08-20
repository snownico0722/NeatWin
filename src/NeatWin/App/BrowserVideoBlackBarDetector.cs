using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using NeatWin.Core;
using NeatWin.Windows;

namespace NeatWin.App;

/// <summary>
/// Conservative screen-space browser video detector. It never injects into the browser or reads
/// DOM/player state. Sparse bright pixels are treated as overlay noise so subtitles and danmaku do
/// not become false content boundaries; multiple frames must agree before a hint is accepted.
/// </summary>
internal sealed class BrowserVideoBlackBarDetector
{
    private const int FrameCount = 3;
    private const int FrameDelayMilliseconds = 28;
    private const int RepeatGuardMilliseconds = 20_000;

    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "vivaldi", "opera", "opera_gx", "arc",
    };

    private readonly Dictionary<nint, Attempt> _attempts = new();

    internal VideoBlackBarHint? TryDetect(IReadOnlyList<VisibleWindow> visibleWindows)
    {
        var candidate = visibleWindows
            .Where(item => item.Window.IsManageable && item.Window.IsResizable && item.VisibleRatio >= 0.72)
            .Where(item => IsBrowserWindow(item.Window.Handle))
            .OrderByDescending(item => item.Window.IsForeground)
            .ThenBy(item => item.Window.ZOrder)
            .FirstOrDefault();

        if (candidate is null || !TryGetClientScreenRect(candidate.Window.Handle, out var clientRect))
        {
            return null;
        }

        if (clientRect.Width < 640 || clientRect.Height < 360)
        {
            return null;
        }

        var workArea = candidate.Window.WorkArea;
        if (!workArea.IsEmpty &&
            (clientRect.Left < workArea.Left - 2 || clientRect.Top < workArea.Top - 2 ||
             clientRect.Right > workArea.Right + 2 || clientRect.Bottom > workArea.Bottom + 2))
        {
            return null;
        }

        var detections = new List<DetectedBars>(FrameCount);
        for (var frameIndex = 0; frameIndex < FrameCount; frameIndex++)
        {
            var detected = CaptureAndAnalyze(clientRect);
            if (detected is null)
            {
                return null;
            }

            detections.Add(detected);
            if (frameIndex + 1 < FrameCount)
            {
                Thread.Sleep(FrameDelayMilliseconds);
            }
        }

        if (!StableAcrossFrames(detections, clientRect))
        {
            return null;
        }

        if (_attempts.TryGetValue(candidate.Window.Handle, out var previousAttempt))
        {
            var age = Environment.TickCount64 - previousAttempt.Tick;
            if (age <= RepeatGuardMilliseconds &&
                ApproximatelyMatches(candidate.Window.VisualRect, previousAttempt.TargetVisualRect, 12))
            {
                // The same bars survived at the geometry NeatWin just tried. They are likely baked
                // into the media or the player does not respond to browser-window aspect changes.
                return null;
            }

            if (!ApproximatelyMatches(candidate.Window.VisualRect, previousAttempt.TargetVisualRect, 28))
            {
                _attempts.Remove(candidate.Window.Handle);
            }
        }

        var orientation = detections[0].Orientation;
        var viewport = MedianRect(detections.Select(item => item.ViewportClientRect).ToArray());
        var aspect = Median(detections.Select(item => item.ContentAspectRatio).ToArray());
        var confidence = detections.Min(item => item.Confidence);

        return new VideoBlackBarHint(
            candidate.Window.Handle,
            orientation,
            aspect,
            new RectI(
                clientRect.Left + viewport.Left,
                clientRect.Top + viewport.Top,
                viewport.Width,
                viewport.Height),
            confidence);
    }

    internal void RecordApplied(nint hwnd, RectI targetVisualRect) =>
        _attempts[hwnd] = new Attempt(targetVisualRect, Environment.TickCount64);

    private static bool IsBrowserWindow(nint hwnd)
    {
        try
        {
            _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0)
            {
                return false;
            }

            using var process = Process.GetProcessById((int)processId);
            return BrowserProcesses.Contains(process.ProcessName);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetClientScreenRect(nint hwnd, out RectI rect)
    {
        if (!NativeMethods.GetClientRect(hwnd, out var client))
        {
            rect = default;
            return false;
        }

        var topLeft = new NativeMethods.Point { X = client.Left, Y = client.Top };
        var bottomRight = new NativeMethods.Point { X = client.Right, Y = client.Bottom };
        if (!NativeMethods.ClientToScreen(hwnd, ref topLeft) ||
            !NativeMethods.ClientToScreen(hwnd, ref bottomRight))
        {
            rect = default;
            return false;
        }

        rect = RectI.FromEdges(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        return !rect.IsEmpty;
    }

    private static DetectedBars? CaptureAndAnalyze(RectI clientRect)
    {
        try
        {
            using var bitmap = new Bitmap(clientRect.Width, clientRect.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    clientRect.Left,
                    clientRect.Top,
                    0,
                    0,
                    bitmap.Size,
                    CopyPixelOperation.SourceCopy);
            }

            var frame = CopyPixels(bitmap);
            var horizontal = AnalyzeHorizontal(frame);
            var vertical = AnalyzeVertical(frame);
            if (horizontal is null)
            {
                return vertical;
            }
            if (vertical is null)
            {
                return horizontal;
            }

            var confidenceDelta = horizontal.Confidence - vertical.Confidence;
            return Math.Abs(confidenceDelta) < 0.08
                ? null
                : confidenceDelta > 0 ? horizontal : vertical;
        }
        catch
        {
            // Protected video, unusual display paths and transient capture failures are a clean
            // "no detection". V1 must never resize a window because capture was uncertain.
            return null;
        }
    }

    private static FramePixels CopyPixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return new FramePixels(bitmap.Width, bitmap.Height, stride, bytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static DetectedBars? AnalyzeHorizontal(FramePixels frame)
    {
        var lineStep = Math.Max(1, frame.Height / 320);
        var metrics = new List<IndexedMetric>();
        for (var y = 0; y < frame.Height; y += lineStep)
        {
            metrics.Add(new IndexedMetric(y, MeasureRow(frame, y)));
        }

        var runs = FindDarkRuns(metrics, lineStep, frame.Height, frame.Height)
            .Where(run => run.Length >= Math.Max(8, frame.Height * 0.022))
            .Where(run => run.Length <= frame.Height * 0.24)
            .ToArray();

        DetectedBars? best = null;
        foreach (var top in runs.Where(run => run.End < frame.Height * 0.54))
        {
            foreach (var bottom in runs.Where(run => run.Start > frame.Height * 0.46))
            {
                if (bottom.Start <= top.End)
                {
                    continue;
                }

                var symmetryError = Math.Abs(top.Length - bottom.Length) /
                                    (double)Math.Max(top.Length, bottom.Length);
                if (symmetryError > 0.32)
                {
                    continue;
                }

                var topSpan = LongestMostlyDarkSpanRow(frame, (top.Start + top.End) / 2);
                var bottomSpan = LongestMostlyDarkSpanRow(frame, (bottom.Start + bottom.End) / 2);
                var viewportLeft = Math.Max(topSpan.Start, bottomSpan.Start);
                var viewportRight = Math.Min(topSpan.End, bottomSpan.End);
                var viewportWidth = viewportRight - viewportLeft;
                if (viewportWidth < frame.Width * 0.68)
                {
                    continue;
                }

                var contentHeight = bottom.Start - top.End;
                var videoHeight = bottom.End - top.Start;
                if (contentHeight <= 0 || videoHeight < frame.Height * 0.50)
                {
                    continue;
                }

                var barRatio = (top.Length + bottom.Length) / (double)videoHeight;
                if (barRatio is < 0.055 or > 0.36)
                {
                    continue;
                }

                // A protected/blank video or a genuinely black scene should not become a resize
                // signal. Test several interior lines and require visible texture/light in most.
                var interiorSignal = InteriorHorizontalSignal(frame, top.End, bottom.Start);
                if (interiorSignal < 0.58)
                {
                    continue;
                }

                var aspect = viewportWidth / (double)contentHeight;
                if (aspect is < 0.70 or > 4.20)
                {
                    continue;
                }

                var confidence = Math.Clamp(
                    0.83 + ((1 - symmetryError) * 0.07) +
                    (Math.Min(1.0, viewportWidth / (double)frame.Width) * 0.05) +
                    (interiorSignal * 0.03),
                    0,
                    0.97);
                var detected = new DetectedBars(
                    VideoBlackBarOrientation.Horizontal,
                    aspect,
                    RectI.FromEdges(viewportLeft, top.Start, viewportRight, bottom.End),
                    confidence);

                if (best is null || detected.Confidence > best.Confidence)
                {
                    best = detected;
                }
            }
        }

        return best;
    }

    private static DetectedBars? AnalyzeVertical(FramePixels frame)
    {
        var lineStep = Math.Max(1, frame.Width / 360);
        var metrics = new List<IndexedMetric>();
        for (var x = 0; x < frame.Width; x += lineStep)
        {
            metrics.Add(new IndexedMetric(x, MeasureColumn(frame, x)));
        }

        var runs = FindDarkRuns(metrics, lineStep, frame.Width, frame.Width)
            .Where(run => run.Length >= Math.Max(8, frame.Width * 0.022))
            .Where(run => run.Length <= frame.Width * 0.24)
            .ToArray();

        DetectedBars? best = null;
        foreach (var left in runs.Where(run => run.End < frame.Width * 0.54))
        {
            foreach (var right in runs.Where(run => run.Start > frame.Width * 0.46))
            {
                if (right.Start <= left.End)
                {
                    continue;
                }

                var symmetryError = Math.Abs(left.Length - right.Length) /
                                    (double)Math.Max(left.Length, right.Length);
                if (symmetryError > 0.32)
                {
                    continue;
                }

                var leftSpan = LongestMostlyDarkSpanColumn(frame, (left.Start + left.End) / 2);
                var rightSpan = LongestMostlyDarkSpanColumn(frame, (right.Start + right.End) / 2);
                var viewportTop = Math.Max(leftSpan.Start, rightSpan.Start);
                var viewportBottom = Math.Min(leftSpan.End, rightSpan.End);
                var viewportHeight = viewportBottom - viewportTop;
                if (viewportHeight < frame.Height * 0.58)
                {
                    continue;
                }

                var contentWidth = right.Start - left.End;
                var videoWidth = right.End - left.Start;
                if (contentWidth <= 0 || videoWidth < frame.Width * 0.50)
                {
                    continue;
                }

                var barRatio = (left.Length + right.Length) / (double)videoWidth;
                if (barRatio is < 0.055 or > 0.36)
                {
                    continue;
                }

                var interiorSignal = InteriorVerticalSignal(frame, left.End, right.Start);
                if (interiorSignal < 0.58)
                {
                    continue;
                }

                var aspect = contentWidth / (double)viewportHeight;
                if (aspect is < 0.55 or > 3.40)
                {
                    continue;
                }

                var confidence = Math.Clamp(
                    0.82 + ((1 - symmetryError) * 0.07) +
                    (Math.Min(1.0, viewportHeight / (double)frame.Height) * 0.05) +
                    (interiorSignal * 0.03),
                    0,
                    0.96);
                var detected = new DetectedBars(
                    VideoBlackBarOrientation.Vertical,
                    aspect,
                    RectI.FromEdges(left.Start, viewportTop, right.End, viewportBottom),
                    confidence);

                if (best is null || detected.Confidence > best.Confidence)
                {
                    best = detected;
                }
            }
        }

        return best;
    }

    private static IReadOnlyList<DarkRun> FindDarkRuns(
        IReadOnlyList<IndexedMetric> lines,
        int lineStep,
        int coordinateLimit,
        int referenceSize)
    {
        var result = new List<DarkRun>();
        var gapTolerance = Math.Max(lineStep * 2, (int)Math.Round(referenceSize * 0.010));
        int? start = null;
        var lastDark = 0;

        foreach (var line in lines)
        {
            if (IsOverlayTolerantDark(line.Metric))
            {
                start ??= line.Index;
                lastDark = line.Index;
                continue;
            }

            if (start is int runStart && line.Index - lastDark <= gapTolerance)
            {
                // Small bright holes are ignored. This is the subtitle/danmaku filter at the
                // scan-line level: sparse moving text must not split an otherwise stable black bar.
                continue;
            }

            if (start is int completedStart)
            {
                result.Add(new DarkRun(completedStart, Math.Min(coordinateLimit, lastDark + lineStep)));
                start = null;
            }
        }

        if (start is int finalStart)
        {
            result.Add(new DarkRun(finalStart, Math.Min(coordinateLimit, lastDark + lineStep)));
        }

        return result;
    }

    private static bool IsOverlayTolerantDark(LineMetric metric) =>
        metric.DarkFraction >= 0.82 &&
        metric.MedianLuma <= 30 &&
        metric.BrightFraction <= 0.17;

    private static LineMetric MeasureRow(FramePixels frame, int y)
    {
        var start = (int)Math.Round(frame.Width * 0.035);
        var end = Math.Max(start + 1, (int)Math.Round(frame.Width * 0.965));
        var step = Math.Max(1, (end - start) / 360);
        return Measure(start, end, step, coordinate => Pixel(frame, coordinate, y));
    }

    private static LineMetric MeasureColumn(FramePixels frame, int x)
    {
        var start = (int)Math.Round(frame.Height * 0.025);
        var end = Math.Max(start + 1, (int)Math.Round(frame.Height * 0.975));
        var step = Math.Max(1, (end - start) / 300);
        return Measure(start, end, step, coordinate => Pixel(frame, x, coordinate));
    }

    private static LineMetric Measure(int start, int end, int step, Func<int, int> lumaAt)
    {
        var histogram = new int[256];
        var count = 0;
        var dark = 0;
        var bright = 0;
        for (var coordinate = start; coordinate < end; coordinate += step)
        {
            var luma = lumaAt(coordinate);
            histogram[luma]++;
            count++;
            if (luma <= 42)
            {
                dark++;
            }
            if (luma >= 105)
            {
                bright++;
            }
        }

        if (count == 0)
        {
            return default;
        }

        var medianTarget = (count + 1) / 2;
        var cumulative = 0;
        var median = 0;
        for (var value = 0; value < histogram.Length; value++)
        {
            cumulative += histogram[value];
            if (cumulative >= medianTarget)
            {
                median = value;
                break;
            }
        }

        return new LineMetric(
            dark / (double)count,
            bright / (double)count,
            median);
    }

    private static SpanRange LongestMostlyDarkSpanRow(FramePixels frame, int y) =>
        LongestMostlyDarkSpan(frame.Width, coordinate => Pixel(frame, coordinate, y));

    private static SpanRange LongestMostlyDarkSpanColumn(FramePixels frame, int x) =>
        LongestMostlyDarkSpan(frame.Height, coordinate => Pixel(frame, x, coordinate));

    private static SpanRange LongestMostlyDarkSpan(int length, Func<int, int> lumaAt)
    {
        var step = Math.Max(1, length / 520);
        var maxBrightGap = Math.Max(3, (int)Math.Round(length * 0.028));
        var bestStart = 0;
        var bestEnd = 0;
        int? start = null;
        var lastDark = 0;

        for (var coordinate = 0; coordinate < length; coordinate += step)
        {
            var dark = lumaAt(coordinate) <= 48;
            if (dark)
            {
                start ??= coordinate;
                lastDark = coordinate;
                continue;
            }

            if (start.HasValue && coordinate - lastDark <= maxBrightGap)
            {
                // Merge short bright gaps caused by text glyphs, captions, danmaku and controls.
                continue;
            }

            if (start is int runStart && lastDark - runStart > bestEnd - bestStart)
            {
                bestStart = runStart;
                bestEnd = Math.Min(length, lastDark + step);
            }
            start = null;
        }

        if (start is int finalStart && lastDark - finalStart > bestEnd - bestStart)
        {
            bestStart = finalStart;
            bestEnd = Math.Min(length, lastDark + step);
        }

        return new SpanRange(bestStart, bestEnd);
    }

    private static double InteriorHorizontalSignal(FramePixels frame, int top, int bottom)
    {
        var signal = 0.0;
        var samples = 0;
        foreach (var fraction in new[] { 0.22, 0.38, 0.50, 0.62, 0.78 })
        {
            var y = Math.Clamp(top + (int)Math.Round((bottom - top) * fraction), 0, frame.Height - 1);
            var metric = MeasureRow(frame, y);
            signal += Math.Clamp((1.0 - metric.DarkFraction) + (metric.BrightFraction * 0.7), 0, 1);
            samples++;
        }
        return samples == 0 ? 0 : signal / samples;
    }

    private static double InteriorVerticalSignal(FramePixels frame, int left, int right)
    {
        var signal = 0.0;
        var samples = 0;
        foreach (var fraction in new[] { 0.22, 0.38, 0.50, 0.62, 0.78 })
        {
            var x = Math.Clamp(left + (int)Math.Round((right - left) * fraction), 0, frame.Width - 1);
            var metric = MeasureColumn(frame, x);
            signal += Math.Clamp((1.0 - metric.DarkFraction) + (metric.BrightFraction * 0.7), 0, 1);
            samples++;
        }
        return samples == 0 ? 0 : signal / samples;
    }

    private static int Pixel(FramePixels frame, int x, int y)
    {
        x = Math.Clamp(x, 0, frame.Width - 1);
        y = Math.Clamp(y, 0, frame.Height - 1);
        var offset = (y * frame.Stride) + (x * 4);
        var b = frame.Bytes[offset];
        var g = frame.Bytes[offset + 1];
        var r = frame.Bytes[offset + 2];
        return (77 * r + 150 * g + 29 * b) >> 8;
    }

    private static bool StableAcrossFrames(IReadOnlyList<DetectedBars> items, RectI clientRect)
    {
        if (items.Count != FrameCount || items.Any(item => item.Orientation != items[0].Orientation))
        {
            return false;
        }

        var medianRect = MedianRect(items.Select(item => item.ViewportClientRect).ToArray());
        var coordinateTolerance = Math.Max(5, (int)Math.Round(Math.Min(clientRect.Width, clientRect.Height) * 0.018));
        var medianAspect = Median(items.Select(item => item.ContentAspectRatio).ToArray());

        return items.All(item =>
            Math.Abs(item.ViewportClientRect.Left - medianRect.Left) <= coordinateTolerance &&
            Math.Abs(item.ViewportClientRect.Top - medianRect.Top) <= coordinateTolerance &&
            Math.Abs(item.ViewportClientRect.Right - medianRect.Right) <= coordinateTolerance &&
            Math.Abs(item.ViewportClientRect.Bottom - medianRect.Bottom) <= coordinateTolerance &&
            Math.Abs(item.ContentAspectRatio - medianAspect) / medianAspect <= 0.035);
    }

    private static RectI MedianRect(IReadOnlyList<RectI> rects) => RectI.FromEdges(
        Median(rects.Select(rect => (double)rect.Left).ToArray()),
        Median(rects.Select(rect => (double)rect.Top).ToArray()),
        Median(rects.Select(rect => (double)rect.Right).ToArray()),
        Median(rects.Select(rect => (double)rect.Bottom).ToArray()));

    private static int Median(double[] values)
    {
        Array.Sort(values);
        return (int)Math.Round(values[values.Length / 2]);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var copy = values.ToArray();
        Array.Sort(copy);
        return copy[copy.Length / 2];
    }

    private static bool ApproximatelyMatches(RectI a, RectI b, int tolerance) =>
        Math.Abs(a.Left - b.Left) <= tolerance && Math.Abs(a.Top - b.Top) <= tolerance &&
        Math.Abs(a.Right - b.Right) <= tolerance && Math.Abs(a.Bottom - b.Bottom) <= tolerance;

    private sealed record DetectedBars(
        VideoBlackBarOrientation Orientation,
        double ContentAspectRatio,
        RectI ViewportClientRect,
        double Confidence);

    private sealed record Attempt(RectI TargetVisualRect, long Tick);
    private sealed record FramePixels(int Width, int Height, int Stride, byte[] Bytes);
    private readonly record struct LineMetric(double DarkFraction, double BrightFraction, int MedianLuma);
    private readonly record struct IndexedMetric(int Index, LineMetric Metric);
    private readonly record struct DarkRun(int Start, int End)
    {
        internal int Length => End - Start;
    }
    private readonly record struct SpanRange(int Start, int End);
}

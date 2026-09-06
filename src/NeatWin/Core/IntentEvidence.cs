namespace NeatWin.Core;

// Geometry only. A stable endpoint is an observation, NOT a label saying the layout is good.
public sealed record WindowAdjustmentObservation(
    DateTimeOffset Time, string Session, int WindowId,
    RectI Start, RectI End, RectI StartWorkArea, RectI WorkArea,
    uint Dpi, long DurationMilliseconds, RectI[] Neighbors, string Outcome);

public sealed record IntentReferenceSample(
    DateTimeOffset Time, string Context, double GapDip, bool Horizontal);

public sealed record IntentReferenceDocument(int Version, IntentReferenceSample[] Samples)
{
    public static IntentReferenceDocument Empty => new(1, []);
}

public readonly record struct IntentHint(int GapPixels, double HorizontalPreference, double Confidence);

public static class IntentEvidence
{
    public const int MaximumSamples = 128;

    public static string ContextFor(RectI area) => (area.Width / (double)Math.Max(1, area.Height)) switch
    {
        < 0.9 => "portrait",
        >= 2.4 => "wide",
        _ => "standard",
    };

    public static bool IsValidRect(RectI rect) =>
        rect.Width is > 0 and <= 100_000 && rect.Height is > 0 and <= 100_000 &&
        Math.Abs((long)rect.X) <= 1_000_000 && Math.Abs((long)rect.Y) <= 1_000_000;

    public static IntentReferenceSample? Extract(WindowAdjustmentObservation observation)
    {
        if (observation.Outcome != "stable" || observation.StartWorkArea != observation.WorkArea ||
            !IsValidRect(observation.Start) || !IsValidRect(observation.End) ||
            !IsValidRect(observation.WorkArea) || observation.Dpi is < 48 or > 768 ||
            observation.DurationMilliseconds is < 100 or > 120_000 ||
            observation.End.Intersect(observation.WorkArea).Area != observation.End.Area)
            return null;

        var scale = observation.Dpi / 96.0;
        IntentReferenceSample? best = null;
        foreach (var neighbor in (observation.Neighbors ?? []).Take(24))
        {
            if (!IsValidRect(neighbor)) continue;
            foreach (var horizontal in new[] { true, false })
            {
                var end = Relation(observation.End, neighbor, horizontal);
                var start = Relation(observation.Start, neighbor, horizontal);
                if (end.Overlap < 0.5 || end.Gap < 0 || end.Gap > 48 * scale) continue;
                // Evidence is the deliberate completion of a relation, not simply standing still.
                var improved = Math.Abs(start.Gap - end.Gap) >= 8 * scale ||
                    (start.Alignment - end.Alignment >= 12 * scale && end.Alignment <= 4 * scale);
                if (!improved) continue;
                var candidate = new IntentReferenceSample(observation.Time, ContextFor(observation.WorkArea),
                    Math.Clamp(end.Gap / scale, 0, 48), horizontal);
                if (best is null || candidate.GapDip < best.GapDip) best = candidate;
            }
        }
        return best;
    }

    public static IntentReferenceDocument Normalize(IntentReferenceDocument? document, DateTimeOffset now)
    {
        if (document?.Version != 1) return IntentReferenceDocument.Empty;
        return new(1, (document.Samples ?? [])
            .Where(s => s is not null && s.Time <= now && s.Time >= now.AddDays(-30) &&
                double.IsFinite(s.GapDip) && s.GapDip is >= 0 and <= 48 &&
                s.Context is "wide" or "standard" or "portrait")
            .OrderBy(s => s.Time).TakeLast(MaximumSamples).ToArray());
    }

    public static IntentReferenceDocument Add(IntentReferenceDocument document, IntentReferenceSample sample,
        DateTimeOffset now)
    {
        var clean = Normalize(document, now);
        // Many corrections during one short adjustment session must not look like repeated preference.
        if (clean.Samples.Any(s => s.Context == sample.Context &&
            Math.Abs((s.Time - sample.Time).TotalSeconds) < 30)) return clean;
        return Normalize(new(1, [.. clean.Samples, sample]), now);
    }

    public static IntentHint Resolve(IntentReferenceDocument? document, RectI area, uint dpi, DateTimeOffset now)
    {
        var scale = Math.Clamp(dpi, 48u, 768u) / 96.0;
        var samples = Normalize(document, now).Samples.Where(s => s.Context == ContextFor(area)).ToArray();
        var fallback = new IntentHint((int)Math.Round(8 * scale), 0, 0);
        if (samples.Length < 6 || samples.Select(s => s.Time.ToUnixTimeSeconds() / 300).Distinct().Count() < 3)
            return fallback;
        var sorted = samples.Select(s => s.GapDip).Order().ToArray();
        var median = sorted[sorted.Length / 2];
        var deviation = sorted.Select(x => Math.Abs(x - median)).Order().ElementAt(sorted.Length / 2);
        var effectiveCount = samples.Sum(s => Math.Pow(0.5, (now - s.Time).TotalDays / 14));
        var confidence = Math.Min(0.25, effectiveCount / 80) / (1 + deviation / 8);
        // At most 25% reference influence; never import learned preserve/movement/resize penalties.
        var gapDip = Math.Clamp(8 + confidence * (median - 8), 6, 14);
        var horizontal = samples.Average(s => s.Horizontal ? 1.0 : -1.0);
        return new((int)Math.Round(gapDip * scale), horizontal, confidence);
    }

    private static (double Gap, double Overlap, double Alignment) Relation(RectI a, RectI b, bool horizontal)
    {
        if (horizontal)
            return (a.Left + a.Width / 2.0 < b.Left + b.Width / 2.0 ? b.Left - a.Right : a.Left - b.Right,
                a.VerticalOverlapRatio(b), Math.Min(Math.Abs(a.Top - b.Top), Math.Abs(a.Bottom - b.Bottom)));
        return (a.Top + a.Height / 2.0 < b.Top + b.Height / 2.0 ? b.Top - a.Bottom : a.Top - b.Bottom,
            a.HorizontalOverlapRatio(b), Math.Min(Math.Abs(a.Left - b.Left), Math.Abs(a.Right - b.Right)));
    }
}

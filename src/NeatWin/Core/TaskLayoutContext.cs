namespace NeatWin.Core;

// Explicit preferences, not inferred satisfaction or a personality diagnosis.
// Defaults are engineering hypotheses; see docs/HUMAN_FACTORS_V3.md.
public sealed record TaskLayoutProfile(bool AllowUsefulResize = true, bool AllowPeripheralBleed = true,
    double MaximumBleedDip = 24, double JointVisibilityWeight = 1,
    double SpatialContinuityWeight = 1, double ComfortableWidthDip = 1120,
    double ComfortableHeightDip = 800)
{
    public TaskLayoutProfile Normalize() => this with
    {
        MaximumBleedDip = Bound(MaximumBleedDip, 24, 0, 48),
        JointVisibilityWeight = Bound(JointVisibilityWeight, 1, .5, 2),
        SpatialContinuityWeight = Bound(SpatialContinuityWeight, 1, .5, 2),
        ComfortableWidthDip = Bound(ComfortableWidthDip, 1120, 480, 2400),
        ComfortableHeightDip = Bound(ComfortableHeightDip, 800, 320, 1600),
    };
    private static double Bound(double v, double fallback, double min, double max) =>
        Math.Clamp(double.IsFinite(v) ? v : fallback, min, max);
}

public sealed record TaskDisplay(nint Monitor, RectI WorkArea, RectI Bounds);

// Optional physical calibration, not inferred from DPI or another device's specifications.
public sealed record ViewingCalibration(RectI WorkArea, uint Dpi, double WidthMillimeters,
    double DistanceMillimeters)
{
    public bool Matches(RectI area, uint dpi) => area == WorkArea && dpi == Dpi &&
        double.IsFinite(WidthMillimeters) && WidthMillimeters is >= 150 and <= 4000 &&
        double.IsFinite(DistanceMillimeters) && DistanceMillimeters is >= 200 and <= 3000;
    public double HorizontalAngle(double x, RectI area) =>
        Math.Atan((x - (area.Left + area.Width / 2.0)) / area.Width * WidthMillimeters /
            DistanceMillimeters) * 180 / Math.PI;
}

public enum TaskRelation { Automatic, JointView, Alternating, Integrated }
public sealed record TaskPairHint(nint First, nint Second, TaskRelation Relation);
public sealed record TaskWindowHint(nint Handle, bool PassiveVisual = false,
    bool ProtectPeriphery = false, double? UsefulWidthDip = null, double? UsefulHeightDip = null);
public sealed record TaskLayoutContext(TaskLayoutProfile? Profile = null, TaskDisplay[]? Displays = null,
    ViewingCalibration? Calibration = null, TaskPairHint[]? PairHints = null,
    TaskWindowHint[]? WindowHints = null, ManualWindowGesture? RecentGesture = null,
    bool VerifiedRepeat = false, string[]? RejectedLayouts = null, VideoBlackBarHint? VideoHint = null)
{
    public TaskLayoutProfile Preferences => (Profile ?? new()).Normalize();
}

/// <summary>Bounded session feedback, never satisfaction learning. No disk persistence.</summary>
public sealed class TaskLayoutSession
{
    private string? _pending;
    private string? _verified;
    private string[] _pendingGroups = [];
    private readonly Queue<string> _rejected = new();
    private ManualWindowGesture? _gesture;
    private DateTimeOffset _gestureTime;
    private DateTimeOffset _appliedTime;

    public void Gesture(ManualWindowGesture gesture, DateTimeOffset now)
    {
        _gesture = gesture; _gestureTime = now; _verified = null; _pending = null;
        // A follow-up may be a task change, not rejection.
        _pendingGroups = [];
    }

    public TaskLayoutContext Capture(IReadOnlyList<WindowSnapshot> windows, TaskLayoutProfile profile,
        TaskDisplay[] displays, ViewingCalibration? calibration, DateTimeOffset now) =>
        new(profile, displays, calibration, RecentGesture: now - _gestureTime <= TimeSpan.FromSeconds(20) ? _gesture : null,
            VerifiedRepeat: _verified == Fingerprint(windows) && now - _appliedTime < TimeSpan.FromMinutes(5),
            RejectedLayouts: _rejected.ToArray());

    public void Requested(IReadOnlyList<WindowSnapshot> before, IntentLayoutPlan plan, DateTimeOffset now)
    {
        if (plan.Moves.Count == 0 && plan.Layers.Count == 0) return;
        var placed = Project(before, plan);
        _pending = Fingerprint(placed); _verified = null; _appliedTime = now;
        _pendingGroups = plan.Groups.Select(g => LayoutKey(placed.Where(w => g.Handles.Contains(w.Handle)))).ToArray();
    }

    public void Verify(IReadOnlyList<WindowSnapshot> actual)
    {
        if (_pending is null) return; // A no-op verification must not erase a verified layout.
        _verified = _pending == Fingerprint(actual) ? _pending : null;
        _pending = null;
    }

    public void RejectExplicitly()
    {
        foreach (var key in _pendingGroups)
        {
            _rejected.Enqueue(key);
            while (_rejected.Count > 32) _rejected.Dequeue();
        }
        _pendingGroups = []; _pending = null; _verified = null;
    }

    public void Invalidate() { _pending = null; _verified = null; }

    public static string LayoutKey(IEnumerable<WindowSnapshot> windows) => string.Join(";",
        windows.OrderBy(w => w.Handle).Select(w => $"{w.Handle}:{w.ProcessId}:{w.WorkArea}:{w.Dpi}:{w.VisualRect}:{w.ZOrder}"));

    private static string Fingerprint(IEnumerable<WindowSnapshot> windows) => string.Join(";",
        windows.OrderBy(w => w.Handle).Select(w =>
            $"{w.Handle}:{w.ProcessId}:{w.MonitorHandle}:{w.WorkArea}:{w.Dpi}:{w.VisualRect}:{w.ZOrder}:{w.IsForeground}:{w.IsTopmost}:{w.IsManageable}"));

    private static WindowSnapshot[] Project(IReadOnlyList<WindowSnapshot> before, IntentLayoutPlan plan)
    {
        var rects = plan.Moves.ToDictionary(m => m.Window.Handle, m => m.TargetVisualRect);
        var ranks = before.ToDictionary(w => w.Handle, w => w.ZOrder);
        foreach (var layer in plan.Layers)
        {
            var slots = layer.FrontToBack.Select(w => ranks[w.Handle]).Order().ToArray();
            for (var i = 0; i < slots.Length; i++) ranks[layer.FrontToBack[i].Handle] = slots[i];
        }
        return before.Select(w => w with { VisualRect = rects.GetValueOrDefault(w.Handle, w.VisualRect),
            ZOrder = ranks[w.Handle] }).ToArray();
    }
}

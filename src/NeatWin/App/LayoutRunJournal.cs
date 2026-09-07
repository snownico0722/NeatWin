using NeatWin.Core;
using NeatWin.Reference;
using NeatWin.Windows;

namespace NeatWin.App;

/// <summary>Explicit operations only. Requested targets are not evidence of successful placement.</summary>
internal sealed class LayoutRunJournal : IDisposable
{
    private readonly WindowManager _manager;
    private readonly IntentReferenceStore _store;
    private readonly ObservationIdentityTracker _identity = new();
    private readonly string _session = Guid.NewGuid().ToString("N");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 600 };
    private readonly List<Pending> _pending = [];
    internal event Action<LayoutRunObservation>? Completed;

    internal LayoutRunJournal(WindowManager manager, IntentReferenceStore store)
    {
        _manager = manager; _store = store;
        _timer.Tick += (_, _) => CompleteDue();
    }

    internal void MarkGesture(nint handle)
    {
        foreach (var pending in _pending)
            if (pending.Handles.Contains(handle)) pending.Intervened = true;
    }

    internal void Record(IReadOnlyList<WindowSnapshot> before, IntentLayoutPlan plan, string trigger, string[] notes)
    {
        try
        {
            var frame = _identity.Capture(before);
            var ids = before.ToDictionary(w => w.Handle, w => _identity.Id(w));
            var targets = plan.Moves.Select(m => new PlannedWindow(ids[m.Window.Handle], m.TargetVisualRect)).ToArray();
            var layers = plan.Layers.Select(l => l.FrontToBack.Select(w => ids[w.Handle]).ToArray()).ToArray();
            var groups = plan.Groups.Select(g => new PlannedGroup(g.Handles.Select(h => ids[h]).ToArray(),
                g.Selected, g.StackEvidence, g.Candidates)).ToArray();
            var observation = new LayoutRunObservation(2, _session, Guid.NewGuid().ToString("N"), trigger,
                frame, targets, layers, groups, null, "requested-not-yet-verified", notes);
            _store.AppendPlan(observation);
            if (_pending.Count >= 8) _pending.RemoveAt(0);
            foreach (var old in _pending) old.Intervened = true;
            _pending.Add(new(observation, before.Select(w => w.Handle).ToHashSet(), Environment.TickCount64));
            _timer.Start();
        }
        catch { } // Diagnostics are best effort and must not prevent a layout or an undo.
    }

    private void CompleteDue()
    {
        foreach (var pending in _pending.Where(p => Environment.TickCount64 - p.Tick >= 600).ToArray())
        {
            _pending.Remove(pending);
            try
            {
                var frame = _identity.Capture(_manager.Capture());
                var requested = pending.Observation;
                var matches = requested.Targets.All(t => frame.Windows.Any(w => w.Id == t.Id && Near(w.Rect, t.Target))) &&
                    requested.Layers.All(order => frame.Windows.Where(w => order.Contains(w.Id)).OrderBy(w => w.ZOrder)
                        .Select(w => w.Id).SequenceEqual(order));
                var actual = requested with { Actual = frame, Outcome = pending.Intervened || NativeWindowActivity.IsMoving ? "intervened" :
                    matches ? "observed-match" : "observed-difference" };
                _store.AppendPlan(actual);
                Completed?.Invoke(actual);
            }
            catch { }
        }
        if (_pending.Count == 0) _timer.Stop();
    }

    private static bool Near(RectI a, RectI b) => Math.Abs(a.X - b.X) <= 3 && Math.Abs(a.Y - b.Y) <= 3 &&
        Math.Abs(a.Width - b.Width) <= 3 && Math.Abs(a.Height - b.Height) <= 3;

    public void Dispose() { _timer.Stop(); _timer.Dispose(); _pending.Clear(); }
    private sealed class Pending(LayoutRunObservation observation, HashSet<nint> handles, long tick)
    {
        internal LayoutRunObservation Observation { get; } = observation;
        internal HashSet<nint> Handles { get; } = handles;
        internal long Tick { get; } = tick;
        internal bool Intervened { get; set; }
    }
}

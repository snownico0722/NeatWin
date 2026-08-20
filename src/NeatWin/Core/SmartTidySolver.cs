namespace NeatWin.Core;

/// <summary>
/// Compatibility entry point for Smart tidy. The previous per-edge weighted relaxation solver was
/// intentionally replaced by the comfort solver, which preserves size by default and minimizes
/// translation under a fixed set of relationships inferred from the original layout.
/// </summary>
internal static class SmartTidySolver
{
    internal static IReadOnlyList<TidyMove> CreatePlan(
        IReadOnlyList<VisibleWindow> visibleWindows,
        TidyOptions options) =>
        ComfortSmartTidySolver.CreatePlan(visibleWindows, options);
}

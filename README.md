# NeatWin

> V0 prototype: conservative visible-window tidy, intentionally not a tiling window manager.

NeatWin is a small Windows utility that **tidies the windows you are actually looking at** without replacing the normal floating-window desktop with a tiling window manager.

Press **Win + Alt + T** and NeatWin makes conservative geometry corrections: nearby gaps and small overlaps are closed, almost-aligned edges are normalized, and windows already near the usable screen boundary can be expanded or nudged toward it.

## V0 behavior

NeatWin deliberately treats the current desktop arrangement as user intent.

- Only top-level windows on the current visible desktop snapshot are considered.
- A window hidden behind other windows is not pulled back into view just because `IsWindowVisible()` is true.
- Z-order is evaluated front-to-back and foreground rectangles are subtracted from windows behind them.
- Windows with only a tiny exposed strip are ignored.
- Maximized windows, owned dialogs, tool windows and no-activate utility surfaces are not rearranged.
- Windows are never moved across monitors.
- Focus and Z-order are preserved when the tidy plan is applied.
- Small gaps and small overlaps between neighboring windows are converged toward a shared edge.
- Near-matching outer edges are aligned.
- Windows close to a monitor work-area edge may be resized toward that edge.
- Resize is conservative: by default each dimension can change by at most about 12%, and each edge can move by at most 96 physical pixels.
- Distant windows are left alone instead of being forced into a template.

The V0 visibility gate defaults to a 12% exposed-area threshold plus a minimum 40,000 px² largest visible fragment. The active foreground window is kept when it has any visible area.

## Architecture

```text
Global hotkey / tray
        |
        v
Window capture (Win32 + DWM)
        |
        v
Z-order visibility analysis
(rectangle subtraction)
        |
        v
Visible working set
        |
        v
Conservative tidy solver
(move + bounded resize)
        |
        v
DeferWindowPos batch apply
(NOACTIVATE + NOZORDER)
```

The implementation is intentionally split so the geometry and visibility logic can be unit-tested without touching real desktop windows.

### Reference projects

The Windows integration strategy is informed by mature window-management code in **Microsoft PowerToys / FancyZones**, especially its handling of DWM frame bounds, resizable-window checks, monitor work areas and DPI-sensitive placement. The separation between window plumbing and a pure layout engine is also inspired by **Whim**.

NeatWin does not copy their layout behavior: its goal is to preserve an existing floating arrangement and apply the smallest useful correction rather than retile the workspace.

## Build

Requirements:

- Windows 10 2004 or later / Windows 11
- .NET 8 SDK

```powershell
dotnet build src/NeatWin/NeatWin.csproj -c Release
dotnet test tests/NeatWin.Tests/NeatWin.Tests.csproj -c Release
```

Run the built executable and use **Win + Alt + T**. A tray icon also exposes `Tidy visible windows` and `Exit`.

GitHub Actions builds and tests the project on `windows-latest` and publishes a framework-dependent x64 single-file artifact.

## Current limitations

V0 intentionally stays conservative:

- Occlusion is rectangle-based; irregular transparency and shaped windows are not pixel-accurate.
- There is no animation yet; the first target is correctness and predictable geometry.
- The solver recognizes local adjacency rather than constructing a full semantic layout tree.
- Per-app exclusions and user-tunable thresholds do not have a settings UI yet.
- Native window minimum-track sizes are not queried yet; Windows may clamp a requested resize for apps with stricter limits.

These are expected follow-up areas after real desktop testing.

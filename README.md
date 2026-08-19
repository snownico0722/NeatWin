# NeatWin

> V0 prototype: intelligent visible-window tidy, intentionally not a tiling window manager.

NeatWin is a small Windows utility that **tidies the windows you are actually looking at** without replacing the normal floating-window desktop with a tiling window manager.

Use the main-window button or a configurable global hotkey. NeatWin treats the current floating arrangement as user intent, infers nearby relationships, and makes the smallest useful geometric correction it can.

## Smart by default

NeatWin has two solver modes:

- **Smart · intelligent tidy** — the default. It infers a sparse graph of likely relationships between currently visible windows and solves those relationships together instead of applying one `if` rule after another.
- **Classic · threshold rules** — the original deterministic fallback. It is retained for users who explicitly want direct pixel/percentage thresholds.

Smart models each window edge as a geometric variable. Internally it builds weighted constraints for:

- staying close to the original floating arrangement;
- resisting width/height changes independently from position changes;
- closing a likely small gap or small overlap between neighbors;
- aligning near-matching edges in a row or column;
- using nearby monitor work-area boundaries;
- preserving the active foreground window more strongly than a partly occluded window.

Constraint confidence decays smoothly with geometric distance rather than switching abruptly at a single threshold. The solver then performs damped weighted relaxation and projects every iteration back into hard usability limits such as resize budgets, minimum dimensions and monitor work areas.

The approach is conceptually related to constraint-based graph-layout adjustment and overlap-removal work such as Dwyer, Marriott and Stuckey's separation-constraint methods: preserve the original layout as much as possible while satisfying a small, high-confidence set of spatial relationships. NeatWin adds window-specific concerns such as visible-Z-order filtering, focus importance, resizability and monitor work areas.

### Simple Smart controls

Smart deliberately does **not** expose its raw weights, inference radii, iteration count, movement budget or resize percentage in the GUI. Users express intent with only three three-level choices:

- **Tidy strength: Gentle / Balanced / Assertive** — how much the solver is willing to change the current arrangement overall.
- **Hit tendency: Cautious / Balanced / Sensitive** — how readily nearby windows are interpreted as belonging to the same alignment/adjacency structure.
- **Size tendency: Preserve size / Balanced / Expand usage** — whether Smart should prefer moving whole windows or allow more resizing to use nearby free screen space.

The default is **Balanced / Balanced / Balanced**. These choices map to internally calibrated solver parameters. Smart ignores legacy/raw Classic tuning values even if they remain in an older settings file.

When **Classic** is selected, the GUI switches to explicit threshold controls such as neighbor distance, edge-alignment distance, screen-edge distance, movement limit, resize percentage and rule passes.

Off-screen rescue remains a separate behavior switch in both modes. Settings are persisted in `%LOCALAPPDATA%\NeatWin\settings.json` and apply immediately.

## Visible working set

NeatWin deliberately does **not** manage every `WS_VISIBLE` window.

- Top-level windows are captured in Z-order.
- Each candidate is clipped to its monitor work area.
- Rectangles from windows in front are subtracted from windows behind them.
- Fully covered background windows are excluded.
- Windows with only a tiny exposed fragment are ignored.
- The active foreground window is kept whenever it has any visible area.
- Fully off-screen windows are not surfaced unexpectedly.
- A **partly off-screen window that is actually visible** can be rescued into the usable work area.

The default visibility gate uses a 12% exposed-area threshold plus a minimum 40,000 px² largest visible fragment.

## Safety and behavior constraints

- Maximized windows, owned dialogs, tool windows and no-activate utility surfaces are not rearranged.
- Windows are never moved across monitors.
- Focus and Z-order are preserved when the tidy plan is applied.
- Distant windows are left alone instead of being forced into a template.
- Ordinary Smart/Classic changes are bounded by internal/profile or explicit Classic movement and resize budgets.
- **Off-screen rescue is a correctness constraint:** when enabled, it may exceed the ordinary movement budget so a visible window cannot remain stranded outside the work area.
- Oversized resizable windows can be reduced just enough to fit the usable work area; fixed-size windows retain their dimensions and are moved as far into the work area as Windows allows.

## GUI and hotkey

NeatWin opens a small WinForms control window on startup so its running state is obvious.

- **Tidy visible windows** is always available as a mouse button.
- The global shortcut is optional and configurable with Ctrl / Alt / Shift / Win plus A-Z, 0-9 or F1-F12.
- If a shortcut is already owned by another program, NeatWin keeps running and the mouse button still works.
- A failed shortcut change rolls back to the previous working binding.
- Closing the main window hides it to the system tray; double-click the tray icon to reopen it.

## Architecture

```text
GUI / hotkey / tray
        |
        v
Window capture (Win32 + DWM)
        |
        v
Z-order visibility analysis
(rectangle clipping + subtraction)
        |
        v
Visible working set
        |
        +---------------------------+
        |                           |
        v                           v
Smart topology inference       Classic rules
        |
        v
Weighted constraint system
        |
        v
Damped relaxation + projection
        |
        +-------------+-------------+
                      |
                      v
              Rect[] tidy plan
                      |
                      v
             DeferWindowPos batch
             (NOACTIVATE + NOZORDER)
```

The geometry/visibility/solver layers are separated from Win32 application plumbing so core behavior can be unit-tested without touching real desktop windows.

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

GitHub Actions builds and tests the project on `windows-latest` and publishes a framework-dependent x64 single-file artifact.

The Smart V0 regression suite covers visibility filtering, off-screen recovery, exact local gap/overlap convergence, screen-edge anchors, Smart profile behavior, isolation from Classic raw thresholds, a three-window topology case, and Classic fallback.

## Current limitations

V0 still has deliberate boundaries:

- Occlusion is rectangle-based; irregular transparency and shaped windows are not pixel-accurate.
- There is no movement animation yet; correctness and predictable geometry come first.
- Smart inference is geometric rather than semantic: it does not know that one window is a browser and another is a chat client.
- The first Smart solver uses a sparse weighted constraint graph and iterative relaxation rather than a full general-purpose QP package.
- Native window minimum-track sizes are not queried yet; Windows may clamp a requested resize for apps with stricter limits.
- Real multi-monitor, mixed-DPI and unusual application-window behavior still need broader desktop testing.

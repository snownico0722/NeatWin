# WPF desktop migration

## Product decision

Replace the WinForms presentation with a native WPF UI on .NET 10. This is a replacement, not an ElementHost/WindowsFormsHost integration. Do not maintain a second legacy UI, old-runtime target, or old settings migration layer. Preserve the existing window-layout algorithms, undo semantics, recorder privacy rules, and four automation modes.

## Visual direction

A compact desktop workspace with a quiet neutral background, rounded surface cards, restrained blue accent, consistent typography, clear keyboard focus, and light/dark palettes. Use a stable sidebar for Layout, Shortcuts, and Recorder. Keep the four automation modes visible; do not collapse them into a combo box. Important state and errors remain readable without depending on color alone.

## Implementation boundary

Only UI and desktop-host coupling should change. Native window enumeration, layout scoring and plan execution are not to be redesigned for the visual migration. Replace WinForms lifecycle, tray/menu presentation, UI timers and screen helpers with WPF/Win32 implementations as required. Do not add a generic application framework or blanket compatibility wrappers.

## Verification

Build and publish with .NET 10 on Windows. Run existing algorithm tests and geometry stress checks, update UI-specific checks for WPF, and exercise tray wake, hide/exit, hotkeys, all four modes, preferences, recorder export/pause/clear, and per-monitor DPI. A successful compile is not a claim of live desktop behavior being verified.

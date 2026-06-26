# StayAwake — Fullscreen Screensaver Mode

**Date:** 2026-06-26
**Status:** Approved design

## Summary

Add a `-s` flag to StayAwake that opens a borderless black fullscreen window and
animates bouncing colored shapes using Win32 GDI. Keep-awake stays active for the
whole session. The screensaver is dismissed by pressing Esc or any key, or when an
`-h` duration expires — in both cases the window closes, keep-awake is cleared, and
the process exits.

## Goals

- Visual fullscreen screensaver triggered by `-s`.
- Reuse the existing single-file, P/Invoke-based style in `app.cs`.
- Stay NativeAOT-friendly — no new NuGet packages, no GUI framework.
- Keep the existing headless behavior unchanged when `-s` is absent.

## Non-goals

- Multiple effects / effect rotation (only bouncing shapes for now — YAGNI).
- Multi-monitor coverage (primary monitor only).
- Mouse-movement dismissal (mouse is ignored).
- Automated UI tests.

## Behavior

| Aspect | Behavior |
|--------|----------|
| Trigger | `-s` flag. Without it, behavior is exactly as today. |
| Keep-awake | `SetThreadExecutionState` set the same way in both modes; stays on for the whole run. |
| `-h <hours>` | Honored in screensaver mode; on expiry the window closes and the app exits. |
| `-m` (jiggle) | Ignored in screensaver mode (irrelevant). |
| Dismiss | Esc **or any key**. Mouse movement is ignored. |
| On dismiss / time-up | Destroy window, free GDI objects, restore cursor, clear keep-awake, exit process. |
| Window-creation failure | Print an error and fall back to the existing headless keep-awake loop so the core job still works. |

## Architecture

All changes live in `app.cs`, following the existing top-level-statements + P/Invoke
pattern.

1. **Flag parsing** — detect `-s` alongside existing `-m` / `-h` parsing. Keep-awake
   is set before branching into either mode.

2. **Window creation** — register a window class whose `lpfnWndProc` is a static
   `[UnmanagedCallersOnly]` method (the AOT-safe way to provide a native callback).
   Create a `WS_POPUP` window covering the primary monitor
   (`GetSystemMetrics(SM_CXSCREEN/SM_CYSCREEN)`), shown topmost, with the cursor
   hidden via `ShowCursor(false)`.

3. **Animation state** — ~12 shapes. Each shape is a small struct: `X, Y` (position),
   `Dx, Dy` (velocity), `Size`, `Color` (COLORREF), and `Kind` (ellipse or rectangle).
   Initialized randomly with `Random.Shared`.

4. **Render loop** — runs ~60 FPS (`Thread.Sleep(16)`). Each frame:
   - Pump messages with `PeekMessage` / `TranslateMessage` / `DispatchMessage`.
   - Update each shape's position; reverse velocity component on edge collision (bounce).
   - Double-buffer: draw to an off-screen memory DC + compatible bitmap to avoid flicker —
     fill black (`FillRect`), draw each shape (`Ellipse` / `Rectangle` with a
     `CreateSolidBrush` brush), then `BitBlt` the buffer to the window DC.
   - Check `stopAt` (from `-h`) and the quit flag; exit the loop when either fires.

5. **Exit / cleanup** — the WndProc sets a quit flag on `WM_KEYDOWN` (Esc or any key)
   and calls `PostQuitMessage` on `WM_DESTROY`. After the loop: `DestroyWindow`, free
   all GDI objects (brushes, bitmap, DCs) in a `finally`, `ShowCursor(true)`, clear
   execution state with `ES_CONTINUOUS`, and return.

## New P/Invoke surface

`user32`: `RegisterClassEx`, `CreateWindowEx`, `DefWindowProc`, `ShowWindow`,
`PeekMessage`, `TranslateMessage`, `DispatchMessage`, `DestroyWindow`, `PostQuitMessage`,
`GetSystemMetrics`, `ShowCursor`, `GetDC`, `ReleaseDC`, `FillRect`.

`gdi32`: `CreateCompatibleDC`, `CreateCompatibleBitmap`, `SelectObject`, `DeleteDC`,
`CreateSolidBrush`, `DeleteObject`, `Ellipse`, `Rectangle`, `BitBlt`.

No new packages — keeps the lean AOT build intact.

## Error handling

- Window/class registration failure → log to console, fall back to the existing
  headless keep-awake loop.
- All GDI handles released in a `finally` to avoid leaks even on early exit.

## Testing

Interactive native GDI UI — not practical to unit test. Verification is:

1. Build / AOT publish succeeds with no new warnings.
2. Manual run of `-s`, `-s -h <small>`, and the unchanged default/`-m` paths.

## Delivery

After implementation and manual verification, rebuild/publish and **replace the
current published artifact** (`artifacts\app\app.exe`) so the installed `stayawake`
command runs the new screensaver-capable build.

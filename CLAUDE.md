# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

StayAwake is a Windows utility that keeps the machine awake (prevents sleep/display-off)
via the Win32 `SetThreadExecutionState` API. It is a **single-file .NET 10 "file-based app"**:
all source lives in `app.cs`, configured by the `#:property` directives at the top of that
file (`TargetFramework=net10.0`, `RuntimeIdentifier=win-x64`, `PublishAot=true`,
`AllowUnsafeBlocks=true`) — there is no `.csproj`.

## Commands

```powershell
dotnet build app.cs              # compile-check (fast; framework-dependent, not distributable)
dotnet run app.cs -- -m          # run with flags after `--`
```

Flags: `-m` (jiggle mouse every ~30s), `-s` (fullscreen bouncing-shapes screensaver),
`-h <hours>` (stop after N hours; accepts decimals).

- **No automated test suite exists.** This is interactive native GDI UI; verification is
  `dotnet build app.cs` succeeding **plus** a manual run. Treat a clean build + manual
  observation as the acceptance gate.
- **`dotnet run ... -s` blocks** — it opens a fullscreen window held until a keypress, and
  keep-awake loops indefinitely. For a non-interactive smoke test, bound it with a tiny
  duration: `dotnet run app.cs -- -s -h 0.001` (~3.6s, then auto-exits).
- **Publishing the native exe and installing it: see `PUBLISHING.md`.** NativeAOT needs the
  MSVC linker via the VS 2019 Build Tools x64 developer environment, with the VS Installer
  dir on PATH so `vswhere.exe` resolves — `dotnet publish app.cs` from a plain shell fails.
  The installed `stayawake` command is `C:\Users\fu.yu\tools\stayawake.exe`.

## Architecture

Top-level statements drive everything: parse flags → call `SetThreadExecutionState` once →
branch into headless loop or `RunScreensaver` → on exit clear the execution state.

- **Keep-awake invariant (critical):** `ES_CONTINUOUS` must be cleared on *every* exit path,
  or the machine stays pinned awake after the process dies. It is cleared both in the
  `Console.CancelKeyPress` (Ctrl+C) handler and at the unconditional tail after the
  mode branch. Any new exit path must preserve this.
- **Two modes share one keep-awake setup:** headless (default; `-m` adds a `SendInput`
  mouse nudge) vs. screensaver (`-s`). `-m` is intentionally inert under `-s`.
- **Screensaver = hand-rolled Win32 GDI**, no GUI framework. A borderless topmost `WS_POPUP`
  window runs a ~60 FPS double-buffered render loop (off-screen memory DC + compatible
  bitmap → `BitBlt`) animating bouncing shapes. If window creation fails it falls back to
  `FallbackHeadless` so keep-awake still works.

### NativeAOT / P/Invoke conventions (the constraints that shape the code)

- **No NuGet packages** — everything is hand-written P/Invoke, all declared in
  `static class NativeMethods`. Keep it that way to preserve the lean AOT build.
- **The window callback cannot be a managed delegate.** Under NativeAOT the WndProc must be
  a `static [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]` method
  (`ScreenSaver.WndProc`), assigned to `WNDCLASSEX.lpfnWndProc` via a
  `delegate* unmanaged[Stdcall]<...>` function pointer — which is why `AllowUnsafeBlocks` is
  enabled and `RunScreensaver` is `unsafe`. Cross-thread signaling back to the loop uses the
  `volatile bool ScreenSaver.Quit` flag.
- **Interop structs** (`WNDCLASSEX`, `MSG`, `RECT`, `INPUT`, `MOUSEINPUT`) need explicit
  `[StructLayout(LayoutKind.Sequential)]`, and `CharSet.Unicode` + `[MarshalAs(LPWStr)]` on
  string-bearing ones — AOT relies on these being correct.
- **GDI lifecycle:** the back-buffer bitmap is created from the **screen** DC, not the memory
  DC (creating it from the memory DC yields a 1bpp monochrome bitmap — a classic bug). Every
  created GDI object is freed exactly once in the `finally` block; stock objects (e.g.
  `NULL_PEN`) are never deleted.

## Workflow artifacts

`docs/superpowers/specs/` and `docs/superpowers/plans/` hold design specs and implementation
plans from feature work (e.g. the screensaver design + plan). They are reference history, not
build inputs.

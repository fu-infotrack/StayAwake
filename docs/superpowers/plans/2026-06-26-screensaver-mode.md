# Screensaver Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `-s` flag to StayAwake that opens a borderless black fullscreen window animating bouncing colored shapes via Win32 GDI, dismissible by Esc/any key or `-h` expiry, then rebuild and replace the installed `stayawake` command.

**Architecture:** All changes are in the single file-based app `app.cs`, extending the existing top-level-statements + P/Invoke style. Keep-awake is set once before branching; `-s` runs a new `RunScreensaver` routine that creates a `WS_POPUP` fullscreen window with a static `[UnmanagedCallersOnly]` WndProc, then runs a ~60 FPS double-buffered GDI render loop. Without `-s`, the existing headless loop runs unchanged.

**Tech Stack:** C# / .NET 10 file-based app, NativeAOT (win-x64), Win32 user32 + gdi32 via `DllImport`. No new NuGet packages.

## Global Constraints

- Target framework: `net10.0`; RuntimeIdentifier `win-x64`; `PublishAot=true` (existing `#:property` directives at top of `app.cs` — preserve them).
- New constraint: add `#:property AllowUnsafeBlocks=true` (needed for the function-pointer WndProc).
- No new NuGet packages; stay NativeAOT-friendly (use `DllImport` with built-in marshalling, consistent with existing code).
- Existing default / `-m` / `-h` behavior must remain byte-for-byte unchanged when `-s` is absent.
- Verification is build + manual run (no automated tests — interactive native GDI UI).
- Build/run during dev: `dotnet run app.cs -- <args>`. Publish: `dotnet publish app.cs -o artifacts/app`.
- Installed command lives at `C:\Users\fu.yu\tools\stayawake.exe` (`Get-Command stayawake`).

---

### Task 1: Add `-s` flag, mode branch, and screensaver stub

**Files:**
- Modify: `app.cs` (flag parsing near line 7; main loop near lines 33-43; add `RunScreensaver` static local function; add `AllowUnsafeBlocks` property)

**Interfaces:**
- Consumes: existing `stopAt` (`DateTime?`), existing keep-awake setup.
- Produces: `static void RunScreensaver(DateTime? stopAt)` — entry point the `-s` branch calls; later tasks fill in its body.

- [ ] **Step 1: Add the unsafe-blocks property directive**

At the top of `app.cs`, alongside the existing `#:property` lines, add:

```csharp
#:property AllowUnsafeBlocks=true
```

- [ ] **Step 2: Parse the `-s` flag**

After the `jiggleMouse` line (currently line 7), add:

```csharp
bool screensaver = args.Contains("-s");
```

- [ ] **Step 3: Branch into screensaver mode**

Replace the existing main loop (currently lines 33-43):

```csharp
while (stopAt is null || DateTime.Now < stopAt)
{
    if (jiggleMouse)
    {
        MoveMouse(1, 0);
        Thread.Sleep(100);
        MoveMouse(-1, 0);
    }

    Thread.Sleep(30000);
}
```

with:

```csharp
if (screensaver)
{
    RunScreensaver(stopAt);
}
else
{
    while (stopAt is null || DateTime.Now < stopAt)
    {
        if (jiggleMouse)
        {
            MoveMouse(1, 0);
            Thread.Sleep(100);
            MoveMouse(-1, 0);
        }

        Thread.Sleep(30000);
    }
}
```

- [ ] **Step 4: Add a stub `RunScreensaver` static local function**

Immediately after the existing `MoveMouse` static local function (currently ends line 57), add:

```csharp
static void RunScreensaver(DateTime? stopAt)
{
    Console.WriteLine("Screensaver mode (stub) — press Enter to exit");
    Console.ReadLine();
}
```

- [ ] **Step 5: Update the startup banner**

Replace the line (currently line 17):

```csharp
Console.WriteLine($"Mouse jiggle: {(jiggleMouse ? "ON" : "OFF")}");
```

with:

```csharp
Console.WriteLine($"Mode:         {(screensaver ? "Screensaver" : "Headless")}");
Console.WriteLine($"Mouse jiggle: {(jiggleMouse ? "ON" : "OFF")}");
```

- [ ] **Step 6: Build and verify it compiles**

Run: `dotnet run app.cs -- -s`
Expected: prints the banner with `Mode: Screensaver` and `Screensaver mode (stub) — press Enter to exit`; pressing Enter exits cleanly. Then run `dotnet run app.cs -- -m` and confirm the original headless behavior is unchanged (no screensaver), Ctrl+C stops it.

- [ ] **Step 7: Commit**

```bash
git add app.cs
git commit -m "feat: add -s flag and screensaver mode branch (stub)"
```

---

### Task 2: Win32 interop, fullscreen black window, and exit-on-keypress

**Files:**
- Modify: `app.cs` (add `using System.Runtime.CompilerServices;`; flesh out `RunScreensaver`; add `ScreenSaver` static class with WndProc + quit flag; add interop structs and `DllImport`s to `NativeMethods`)

**Interfaces:**
- Consumes: `RunScreensaver(DateTime? stopAt)` stub from Task 1.
- Produces:
  - `static class ScreenSaver` with `public static volatile bool Quit;` and `[UnmanagedCallersOnly(CallConvs=[typeof(CallConvStdcall)])] public static IntPtr WndProc(IntPtr, uint, IntPtr, IntPtr)`.
  - Interop additions on `NativeMethods`: `GetModuleHandle`, `RegisterClassEx`, `CreateWindowEx`, `DefWindowProc`, `ShowWindow`, `PeekMessage`, `TranslateMessage`, `DispatchMessage`, `DestroyWindow`, `PostQuitMessage`, `GetSystemMetrics`, `ShowCursor`.
  - Structs: `WNDCLASSEX`, `MSG`.

- [ ] **Step 1: Add the CompilerServices using**

At the top of `app.cs`, after `using System.Runtime.InteropServices;`, add:

```csharp
using System.Runtime.CompilerServices;
```

- [ ] **Step 2: Add the WndProc + quit-flag holder class**

Add this static class at the end of `app.cs` (after `NativeMethods`):

```csharp
static class ScreenSaver
{
    public static volatile bool Quit;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    public static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case 0x0100: // WM_KEYDOWN — Esc or any key dismisses
                Quit = true;
                return IntPtr.Zero;
            case 0x0002: // WM_DESTROY
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
```

- [ ] **Step 3: Add interop structs**

Add these struct definitions in `app.cs` near the other structs (after `MOUSEINPUT`):

```csharp
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct WNDCLASSEX
{
    public uint cbSize;
    public uint style;
    public IntPtr lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public IntPtr hInstance;
    public IntPtr hIcon;
    public IntPtr hCursor;
    public IntPtr hbrBackground;
    [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
    [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    public IntPtr hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public int pt_x;
    public int pt_y;
}
```

- [ ] **Step 4: Add the new DllImports to NativeMethods**

Inside `static class NativeMethods`, add:

```csharp
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern int ShowCursor(bool bShow);
```

- [ ] **Step 5: Replace the stub `RunScreensaver` with real window creation + message loop**

Replace the entire stub `RunScreensaver` from Task 1 with:

```csharp
static unsafe void RunScreensaver(DateTime? stopAt)
{
    const string className = "StayAwakeScreenSaver";
    IntPtr hInstance = NativeMethods.GetModuleHandle(null);

    var wc = new WNDCLASSEX
    {
        cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
        style = 0x0003, // CS_HREDRAW | CS_VREDRAW
        lpfnWndProc = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&ScreenSaver.WndProc,
        hInstance = hInstance,
        hbrBackground = IntPtr.Zero, // we paint every frame ourselves
        lpszClassName = className,
    };

    if (NativeMethods.RegisterClassEx(ref wc) == 0)
    {
        Console.WriteLine("Screensaver: failed to register window class.");
        return;
    }

    int width = NativeMethods.GetSystemMetrics(0);  // SM_CXSCREEN
    int height = NativeMethods.GetSystemMetrics(1);  // SM_CYSCREEN

    IntPtr hwnd = NativeMethods.CreateWindowEx(
        0x00000008,                 // WS_EX_TOPMOST
        className, "StayAwake",
        0x80000000,                 // WS_POPUP
        0, 0, width, height,
        IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

    if (hwnd == IntPtr.Zero)
    {
        Console.WriteLine("Screensaver: failed to create window.");
        return;
    }

    NativeMethods.ShowWindow(hwnd, 5); // SW_SHOW
    NativeMethods.ShowCursor(false);
    ScreenSaver.Quit = false;

    try
    {
        while (!ScreenSaver.Quit && (stopAt is null || DateTime.Now < stopAt))
        {
            while (NativeMethods.PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, 1)) // PM_REMOVE
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }

            // (Rendering added in Task 3.)
            Thread.Sleep(16);
        }
    }
    finally
    {
        NativeMethods.ShowCursor(true);
        NativeMethods.DestroyWindow(hwnd);
    }
}
```

- [ ] **Step 6: Build and verify the window**

Run: `dotnet run app.cs -- -s`
Expected: a black borderless window fills the primary monitor; the cursor is hidden over it. Pressing Esc (or any key) closes it and the process exits. Run `dotnet run app.cs -- -s -h 0.001` (~3.6s) and confirm it auto-closes after a few seconds without a keypress.

- [ ] **Step 7: Commit**

```bash
git add app.cs
git commit -m "feat: open fullscreen black window with exit-on-keypress for screensaver"
```

---

### Task 3: Bouncing-shapes animation with double-buffered GDI drawing

**Files:**
- Modify: `app.cs` (add gdi32 `DllImport`s + `RECT` struct to interop; add `Shape` struct; add init + render logic inside `RunScreensaver`)

**Interfaces:**
- Consumes: `RunScreensaver` window/loop from Task 2 (`hwnd`, `width`, `height`, the `while` loop).
- Produces:
  - `struct Shape { public double X, Y, Dx, Dy; public int Size; public IntPtr Brush; public bool IsEllipse; }`
  - `struct RECT { public int Left, Top, Right, Bottom; }`
  - gdi32 interop on `NativeMethods`: `GetDC`, `ReleaseDC`, `CreateCompatibleDC`, `CreateCompatibleBitmap`, `SelectObject`, `DeleteDC`, `DeleteObject`, `CreateSolidBrush`, `GetStockObject`, `Ellipse`, `Rectangle`, `BitBlt`, `FillRect`.

- [ ] **Step 1: Add RECT struct and gdi32 / dc DllImports**

Add `RECT` near the other structs:

```csharp
[StructLayout(LayoutKind.Sequential)]
struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
```

Add to `NativeMethods`:

```csharp
    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    public static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    public static extern IntPtr GetStockObject(int i);

    [DllImport("gdi32.dll")]
    public static extern bool Ellipse(IntPtr hdc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    public static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);
```

- [ ] **Step 2: Add the Shape struct**

Add near the other structs in `app.cs`:

```csharp
struct Shape
{
    public double X, Y, Dx, Dy;
    public int Size;
    public IntPtr Brush;
    public bool IsEllipse;
}
```

- [ ] **Step 3: Initialize shapes and the back-buffer (inside RunScreensaver, before the loop)**

In `RunScreensaver`, after `ScreenSaver.Quit = false;` and before the `try`, add:

```csharp
    var rng = Random.Shared;
    const int shapeCount = 12;
    var shapes = new Shape[shapeCount];
    for (int i = 0; i < shapeCount; i++)
    {
        int size = rng.Next(40, 140);
        uint color = (uint)(rng.Next(60, 256) | (rng.Next(60, 256) << 8) | (rng.Next(60, 256) << 16));
        shapes[i] = new Shape
        {
            X = rng.Next(0, Math.Max(1, width - size)),
            Y = rng.Next(0, Math.Max(1, height - size)),
            Dx = (rng.NextDouble() * 6 - 3) is var dx && Math.Abs(dx) < 1 ? 2 : dx,
            Dy = (rng.NextDouble() * 6 - 3) is var dy && Math.Abs(dy) < 1 ? 2 : dy,
            Size = size,
            Brush = NativeMethods.CreateSolidBrush(color),
            IsEllipse = rng.Next(2) == 0,
        };
    }

    IntPtr screenDc = NativeMethods.GetDC(hwnd);
    IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
    IntPtr memBmp = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
    IntPtr oldBmp = NativeMethods.SelectObject(memDc, memBmp);
    IntPtr nullPen = NativeMethods.GetStockObject(8); // NULL_PEN — solid fills, no outline
    NativeMethods.SelectObject(memDc, nullPen);
    IntPtr blackBrush = NativeMethods.CreateSolidBrush(0); // 0x000000
    var fullRect = new RECT { Left = 0, Top = 0, Right = width, Bottom = height };
```

- [ ] **Step 4: Render each frame (inside the loop, replacing the `// (Rendering added in Task 3.)` comment)**

Replace the comment line inside the `while` loop with:

```csharp
            // Update positions and bounce off edges
            for (int i = 0; i < shapeCount; i++)
            {
                ref Shape s = ref shapes[i];
                s.X += s.Dx;
                s.Y += s.Dy;
                if (s.X < 0) { s.X = 0; s.Dx = -s.Dx; }
                else if (s.X + s.Size > width) { s.X = width - s.Size; s.Dx = -s.Dx; }
                if (s.Y < 0) { s.Y = 0; s.Dy = -s.Dy; }
                else if (s.Y + s.Size > height) { s.Y = height - s.Size; s.Dy = -s.Dy; }
            }

            // Draw to the back-buffer: clear black, then each shape
            NativeMethods.FillRect(memDc, ref fullRect, blackBrush);
            for (int i = 0; i < shapeCount; i++)
            {
                Shape s = shapes[i];
                NativeMethods.SelectObject(memDc, s.Brush);
                int left = (int)s.X, top = (int)s.Y, right = left + s.Size, bottom = top + s.Size;
                if (s.IsEllipse)
                    NativeMethods.Ellipse(memDc, left, top, right, bottom);
                else
                    NativeMethods.Rectangle(memDc, left, top, right, bottom);
            }

            // Blit back-buffer to the window
            NativeMethods.BitBlt(screenDc, 0, 0, width, height, memDc, 0, 0, 0x00CC0020); // SRCCOPY
```

- [ ] **Step 5: Free GDI resources in the finally block**

Replace the `finally` block from Task 2 with:

```csharp
    finally
    {
        NativeMethods.SelectObject(memDc, oldBmp);
        NativeMethods.DeleteObject(memBmp);
        NativeMethods.DeleteDC(memDc);
        NativeMethods.ReleaseDC(hwnd, screenDc);
        NativeMethods.DeleteObject(blackBrush);
        for (int i = 0; i < shapeCount; i++)
            NativeMethods.DeleteObject(shapes[i].Brush);
        NativeMethods.ShowCursor(true);
        NativeMethods.DestroyWindow(hwnd);
    }
```

- [ ] **Step 6: Build and verify the animation**

Run: `dotnet run app.cs -- -s`
Expected: ~12 colored circles and squares bounce smoothly around a black fullscreen window, reflecting off all four edges, with no visible flicker. Esc/any key exits; the cursor reappears. Run again with `-s -h 0.002` and confirm it auto-closes.

- [ ] **Step 7: Commit**

```bash
git add app.cs
git commit -m "feat: animate bouncing shapes with double-buffered GDI rendering"
```

---

### Task 4: Robustness — keep-awake integrity and graceful fallback

**Files:**
- Modify: `app.cs` (`RunScreensaver` failure paths; confirm keep-awake clears on exit)

**Interfaces:**
- Consumes: `RunScreensaver` from Task 3; the existing `Console.CancelKeyPress` handler and end-of-program `SetThreadExecutionState(ES_CONTINUOUS)` (lines ~27-31 and ~46).
- Produces: no new public surface; hardens existing behavior.

- [ ] **Step 1: Confirm keep-awake is cleared after screensaver exit**

Verify that after the `if (screensaver) { RunScreensaver(stopAt); }` branch, control falls through to the existing tail of the program. Ensure the final lines still read:

```csharp
Console.WriteLine("Time's up, stopping...");
NativeMethods.SetThreadExecutionState(ExecutionState.ES_CONTINUOUS);
```

If the screensaver branch currently bypasses this, move the `SetThreadExecutionState(ExecutionState.ES_CONTINUOUS)` cleanup so it runs for both modes. Concretely, ensure the end of `app.cs` (after the `if/else` mode block) is:

```csharp
Console.WriteLine("Stopping...");
NativeMethods.SetThreadExecutionState(ExecutionState.ES_CONTINUOUS);
```

(Replacing the headless-only "Time's up" message with a neutral "Stopping..." that fits both modes.)

- [ ] **Step 2: Verify the window-create failure fallback**

Confirm the early `return`s in `RunScreensaver` (class-register failure, window-create failure) print a message and return. Because keep-awake was already set before the branch and is cleared after it (Step 1), a failed screensaver still leaves the machine awake until the process is stopped. Add a clarifying console line so the user knows the core job continues — change the two failure `return`s to:

```csharp
        Console.WriteLine("Screensaver: failed to register window class. Keep-awake still active; press Ctrl+C to stop.");
        FallbackHeadless(stopAt);
        return;
```

and

```csharp
        Console.WriteLine("Screensaver: failed to create window. Keep-awake still active; press Ctrl+C to stop.");
        FallbackHeadless(stopAt);
        return;
```

- [ ] **Step 3: Add the FallbackHeadless helper**

Add this static local function after `RunScreensaver`:

```csharp
static void FallbackHeadless(DateTime? stopAt)
{
    while (stopAt is null || DateTime.Now < stopAt)
        Thread.Sleep(30000);
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet run app.cs -- -s` — confirm the screensaver still works normally (the fallback paths are not hit). Run `dotnet run app.cs -- -m` and the default `dotnet run app.cs` — confirm headless behavior and Ctrl+C cleanup are unchanged, and the closing message now reads "Stopping...".

- [ ] **Step 5: Commit**

```bash
git add app.cs
git commit -m "feat: harden keep-awake cleanup and add headless fallback for screensaver"
```

---

### Task 5: Publish and replace the installed `stayawake` command

**Files:**
- Modify: `artifacts/app/app.exe` (regenerated by publish)
- Replace: `C:\Users\fu.yu\tools\stayawake.exe`

**Interfaces:**
- Consumes: the finished `app.cs` from Task 4.
- Produces: an updated installed executable.

- [ ] **Step 1: AOT-publish the app**

Run: `dotnet publish app.cs -o artifacts/app`
Expected: build succeeds with no errors; `artifacts/app/app.exe` is regenerated.

- [ ] **Step 2: Smoke-test the published binary**

Run: `artifacts/app/app.exe -s`
Expected: the screensaver runs from the published exe; Esc exits. Then `artifacts/app/app.exe -h 0.001` confirms headless still works.

- [ ] **Step 3: Replace the installed command**

Run (PowerShell):

```powershell
Copy-Item -Path artifacts\app\app.exe -Destination C:\Users\fu.yu\tools\stayawake.exe -Force
```

- [ ] **Step 4: Verify the installed command**

Run: `stayawake -s`
Expected: `Get-Command stayawake` still resolves to `C:\Users\fu.yu\tools\stayawake.exe`, and running `stayawake -s` launches the new screensaver; Esc exits.

- [ ] **Step 5: Commit the regenerated artifact**

```bash
git add artifacts/app/app.exe
git commit -m "build: publish screensaver-capable stayawake and replace installed command"
```

---

## Self-Review Notes

- **Spec coverage:** `-s` flag (T1), fullscreen window + Esc/any-key exit + cursor hide (T2), bouncing shapes + double buffering (T3), keep-awake throughout + `-h` honored + cleanup + window-fail fallback (T2/T3/T4), `-m` ignored in screensaver mode (T1 — jiggle only runs in the `else` branch), delivery/replace installed exe (T5). All spec sections map to a task.
- **No placeholders:** every code step contains complete code; the only deferred marker (`// (Rendering added in Task 3.)`) is an explicit, intentional hand-off that Task 3 Step 4 replaces.
- **Type consistency:** `RunScreensaver(DateTime?)`, `Shape`, `ScreenSaver.Quit`, `ScreenSaver.WndProc`, and all `NativeMethods` signatures are used consistently across tasks; `width`/`height`/`hwnd`/`memDc`/`memBmp`/`screenDc`/`blackBrush`/`fullRect`/`shapes`/`shapeCount` are introduced before the loop and referenced inside it and in `finally`.

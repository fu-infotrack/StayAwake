#:property TargetFramework=net10.0
#:property RuntimeIdentifier=win-x64
#:property PublishAot=true
#:property AllowUnsafeBlocks=true

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

bool jiggleMouse = args.Contains("-m");
bool screensaver = args.Contains("-s");

double? hours = null;
int hIdx = Array.IndexOf(args, "-h");
if (hIdx >= 0 && hIdx + 1 < args.Length && double.TryParse(args[hIdx + 1], out double h))
    hours = h;

DateTime? stopAt = hours is not null ? DateTime.Now.AddHours(hours.Value) : null;

Console.WriteLine("StayAwake started");
Console.WriteLine($"Mode:         {(screensaver ? "Screensaver" : "Headless")}");
Console.WriteLine($"Mouse jiggle: {(jiggleMouse ? "ON" : "OFF")}");
Console.WriteLine(stopAt is not null ? $"Stopping at:  {stopAt:T}" : "Duration:     unlimited");
Console.WriteLine("Press Ctrl+C to stop\n");

NativeMethods.SetThreadExecutionState(
    ExecutionState.ES_CONTINUOUS |
    ExecutionState.ES_SYSTEM_REQUIRED |
    ExecutionState.ES_DISPLAY_REQUIRED
);

Console.CancelKeyPress += (s, e) =>
{
    Console.WriteLine("\nStopping...");
    NativeMethods.SetThreadExecutionState(ExecutionState.ES_CONTINUOUS);
};

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

Console.WriteLine("Stopping...");
NativeMethods.SetThreadExecutionState(ExecutionState.ES_CONTINUOUS);

static void MoveMouse(int dx, int dy)
{
    var input = new INPUT
    {
        type = 0,
        mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = 0x0001 }
    };

    NativeMethods.SendInput(1, [input], Marshal.SizeOf<INPUT>());
}

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
        Console.WriteLine("Screensaver: failed to register window class. Keep-awake still active; press Ctrl+C to stop.");
        FallbackHeadless(stopAt);
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
        Console.WriteLine("Screensaver: failed to create window. Keep-awake still active; press Ctrl+C to stop.");
        FallbackHeadless(stopAt);
        return;
    }

    NativeMethods.ShowWindow(hwnd, 5); // SW_SHOW
    NativeMethods.ShowCursor(false);
    ScreenSaver.Quit = false;

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

    try
    {
        while (!ScreenSaver.Quit && (stopAt is null || DateTime.Now < stopAt))
        {
            while (NativeMethods.PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, 1)) // PM_REMOVE
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }

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
            Thread.Sleep(16);
        }
    }
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
}

static void FallbackHeadless(DateTime? stopAt)
{
    while (stopAt is null || DateTime.Now < stopAt)
        Thread.Sleep(30000);
}

[Flags]
enum ExecutionState : uint
{
    ES_CONTINUOUS       = 0x80000000,
    ES_SYSTEM_REQUIRED  = 0x00000001,
    ES_DISPLAY_REQUIRED = 0x00000002
}

[StructLayout(LayoutKind.Sequential)]
struct INPUT
{
    public int type;
    public MOUSEINPUT mi;
}

[StructLayout(LayoutKind.Sequential)]
struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public int mouseData;
    public int dwFlags;
    public int time;
    public IntPtr dwExtraInfo;
}

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

[StructLayout(LayoutKind.Sequential)]
struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

struct Shape
{
    public double X, Y, Dx, Dy;
    public int Size;
    public IntPtr Brush;
    public bool IsEllipse;
}

static class NativeMethods
{
    [DllImport("kernel32.dll")]
    public static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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
}

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

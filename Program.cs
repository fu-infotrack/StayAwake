using System.Runtime.InteropServices;

bool jiggleMouse = args.Length > 0 && args[0] == "--mouse";

Console.WriteLine("StayAwake started");
Console.WriteLine($"Mouse jiggle: {(jiggleMouse ? "ON" : "OFF")}");
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

while (true)
{
    if (jiggleMouse)
    {
        MoveMouse(1, 0);
        Thread.Sleep(100);
        MoveMouse(-1, 0);
    }

    Thread.Sleep(30000);
}

static void MoveMouse(int dx, int dy)
{
    var input = new INPUT
    {
        type = 0,
        mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = 0x0001 }
    };

    NativeMethods.SendInput(1, [input], Marshal.SizeOf<INPUT>());
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

static class NativeMethods
{
    [DllImport("kernel32.dll")]
    public static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}

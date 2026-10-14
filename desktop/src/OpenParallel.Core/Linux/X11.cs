using System.Runtime.InteropServices;

namespace OpenParallel.Core.Linux;

/// <summary>Raises an app's window on X11 (and XWayland) the way a taskbar does: find its
/// window through <c>_NET_WM_PID</c>, then send <c>_NET_ACTIVE_WINDOW</c>. Uses libX11
/// directly, no extra tools. Returns false without X11 (pure Wayland).</summary>
internal static class X11
{
    private const string Lib = "libX11.so.6";

    public static bool Activate(IReadOnlySet<int> pids)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return false;
        try
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return false;
            try
            {
                var root = XDefaultRootWindow(display);
                var clientList = XInternAtom(display, "_NET_CLIENT_LIST", false);
                var wmPid = XInternAtom(display, "_NET_WM_PID", false);
                var active = XInternAtom(display, "_NET_ACTIVE_WINDOW", false);
                var target = IntPtr.Zero;
                foreach (var window in Property(display, root, clientList))
                {
                    var pid = Property(display, (IntPtr)window, wmPid).FirstOrDefault();
                    if (pid != 0 && pids.Contains((int)pid)) target = (IntPtr)window;
                }
                if (target == IntPtr.Zero) return false;
                var message = new ClientMessage
                {
                    Type = 33, // ClientMessage
                    SendEvent = 1,
                    Display = display,
                    Window = target,
                    MessageType = active,
                    Format = 32,
                    Data0 = 2, // from a pager/taskbar, which window managers honour
                };
                const long mask = (1L << 19) | (1L << 20); // SubstructureNotify | SubstructureRedirect
                XSendEvent(display, root, false, (IntPtr)mask, ref message);
                XFlush(display);
                return true;
            }
            finally
            {
                XCloseDisplay(display);
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>A 32-bit-format property as a list of C longs.</summary>
    private static List<long> Property(IntPtr display, IntPtr window, IntPtr atom)
    {
        var result = new List<long>();
        if (XGetWindowProperty(display, window, atom, IntPtr.Zero, (IntPtr)4096, false, IntPtr.Zero,
                out _, out var format, out var count, out _, out var data) != 0 || data == IntPtr.Zero) return result;
        try
        {
            if (format == 32)
            {
                for (var i = 0; i < (long)count; i++) result.Add(Marshal.ReadInt64(data, i * 8));
            }
        }
        finally
        {
            XFree(data);
        }
        return result;
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct ClientMessage
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(8)] public ulong Serial;
        [FieldOffset(16)] public int SendEvent;
        [FieldOffset(24)] public IntPtr Display;
        [FieldOffset(32)] public IntPtr Window;
        [FieldOffset(40)] public IntPtr MessageType;
        [FieldOffset(48)] public int Format;
        [FieldOffset(56)] public long Data0;
        [FieldOffset(64)] public long Data1;
        [FieldOffset(72)] public long Data2;
    }

    [DllImport(Lib)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(Lib)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(Lib)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(Lib)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport(Lib)] private static extern int XFlush(IntPtr display);
    [DllImport(Lib)] private static extern int XFree(IntPtr data);
    [DllImport(Lib)] private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, IntPtr mask, ref ClientMessage message);
    [DllImport(Lib)]
    private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr offset, IntPtr length, bool delete,
        IntPtr type, out IntPtr actualType, out int actualFormat, out ulong count, out ulong bytesAfter, out IntPtr data);
}

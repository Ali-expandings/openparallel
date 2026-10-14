using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FakeApp;

/// <summary>A stand-in for a real app in the Wine test bottle. Its behaviour follows its file
/// name, so one build plays Chrome, an Electron app, Firefox, Telegram and Squirrel's Update.exe.
/// It records what it was started with, keeps one copy per profile like the real apps, opens a
/// real window and closes when asked.</summary>
internal static class Program
{
    private static string logDir = "";

    [STAThread]
    private static int Main(string[] args)
    {
        logDir = Path.Combine(Path.GetTempPath(), "op-fixtures");
        Directory.CreateDirectory(logDir);
        var exe = Environment.ProcessPath ?? "FakeApp.exe";
        var exeName = Path.GetFileName(exe);

        if (exeName.Equals("Update.exe", StringComparison.OrdinalIgnoreCase)) return Squirrel(exe, args);

        var profile = ProfileOf(args) ?? "default";
        using var mutex = new Mutex(true, "OPFixture-" + Hash(exeName.ToLowerInvariant() + "|" + profile.ToLowerInvariant()), out var createdNew);
        Record(new
        {
            @event = createdNew ? "start" : "handoff",
            pid = Environment.ProcessId,
            exe,
            args,
            profile,
            cwd = Environment.CurrentDirectory,
            environment = Environment.GetEnvironmentVariables().Keys.Cast<string>()
                .Where(k => k.EndsWith("_PROPERTIES", StringComparison.OrdinalIgnoreCase) || k.StartsWith("OP_", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(k => k, k => Environment.GetEnvironmentVariable(k)),
        });
        // Like Chrome and Electron: a second launch on the same profile hands off and exits.
        if (!createdNew) return 0;

        RunWindow($"{Path.GetFileNameWithoutExtension(exeName)} — {profile}");
        Record(new { @event = "closed", pid = Environment.ProcessId });
        return 0;
    }

    /// <summary>The profile the real app would use, read from the same switches.</summary>
    private static string? ProfileOf(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--user-data-dir=", StringComparison.Ordinal)) return a["--user-data-dir=".Length..];
            if (a is "--user-data-dir" or "-profile" or "-workdir" && i + 1 < args.Length) return args[i + 1];
        }
        var properties = Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .FirstOrDefault(k => k.EndsWith("_PROPERTIES", StringComparison.OrdinalIgnoreCase));
        return properties is null ? null : Environment.GetEnvironmentVariable(properties);
    }

    /// <summary>Squirrel's Update.exe: start the newest app-x.y.z copy of the named exe.</summary>
    private static int Squirrel(string exe, string[] args)
    {
        var root = Path.GetDirectoryName(exe)!;
        var name = Value(args, "--processStart");
        var startArgs = Value(args, "--process-start-args") ?? "";
        Record(new { @event = "squirrel", pid = Environment.ProcessId, args });
        if (name is null) return 1;
        var newest = Directory.EnumerateDirectories(root, "app-*")
            .OrderByDescending(d => Version.TryParse(Path.GetFileName(d)[4..], out var v) ? v : new Version())
            .First();
        var info = new ProcessStartInfo(Path.Combine(newest, name), startArgs) { UseShellExecute = false, WorkingDirectory = newest };
        Process.Start(info);
        return 0;
    }

    private static string? Value(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void Record(object entry)
    {
        var line = JsonSerializer.Serialize(entry);
        File.AppendAllText(Path.Combine(logDir, $"{Environment.ProcessId}.json"), line + Environment.NewLine);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];

    // MARK: a plain Win32 window

    private static WndProc? keepAlive;

    private static void RunWindow(string title)
    {
        keepAlive = WindowProcedure;
        var instance = GetModuleHandleW(null);
        var windowClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(keepAlive),
            hInstance = instance,
            lpszClassName = "OPFixtureWindow",
            hbrBackground = (IntPtr)(5 + 1),
        };
        RegisterClassExW(ref windowClass);
        var window = CreateWindowExW(0, "OPFixtureWindow", title, 0x00CF0000 | 0x10000000, 120, 120, 520, 260,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        ShowWindow(window, 5);
        while (GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    private static IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == 0x0002) // WM_DESTROY
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int x, y;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEXW windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG message);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}

using System.Runtime.InteropServices;
using Avalonia;

namespace OpenParallel.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Shortcuts for recipes that need environment variables run "OpenParallel.exe
        // --launch <slug>": start (or bring forward) that instance and exit, no window.
        if (args.Length >= 2 && args[0] == "--launch" && OperatingSystem.IsWindows())
            return LaunchInstance(args[1]);

        // One OpenParallel per user: a second start shows the first one's window (even from
        // the tray) and exits.
        if (!OneInstance.TryBecomeFirst()) return 0;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static int LaunchInstance(string slug)
    {
        var service = new OpenParallel.Core.InstanceService(new OpenParallel.Core.Windows.WindowsPlatform());
        var manifest = service.Find(slug);
        if (manifest is null)
        {
            service.Log(slug, "launch failed: no such instance");
            return 1;
        }
        try
        {
            service.OpenOrShow(manifest);
            return 0;
        }
        catch (Exception error)
        {
            service.Log(slug, $"launch failed: {error.Message}");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

        // Wine translates Direct3D to Metal and can draw Avalonia's GPU output
        // blank. Software rendering is slower but always correct.
        if (Platform.IsWine || Environment.GetEnvironmentVariable("OPENPARALLEL_SOFTWARE_RENDERING") == "1")
        {
            builder = builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software]
            });
        }
        return builder;
    }
}

internal static class Platform
{
    /// <summary>True when running under Wine rather than real Windows.</summary>
    public static bool IsWine { get; } = DetectWine();

    private static bool DetectWine()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var ntdll = GetModuleHandleW("ntdll.dll");
        return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
}

/// <summary>A second start of OpenParallel asks the first to show its window, then exits.
/// Windows (and Wine): a named mutex and event. Linux: a Unix socket in XDG_RUNTIME_DIR.</summary>
internal static class OneInstance
{
    private const string MutexName = @"Local\OpenParallel.App";
    private const string EventName = @"Local\OpenParallel.Show";
    private static Mutex? mutex;
    private static System.Net.Sockets.Socket? listener;

    /// <summary>Raised on a background thread when another start asked to be shown.</summary>
    public static event Action? ShowRequested;

    private static string SocketPath =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } run ? run : Path.GetTempPath(),
            $"openparallel-{Environment.UserName}.sock");

    public static bool TryBecomeFirst()
    {
        if (OperatingSystem.IsWindows())
        {
            mutex = new Mutex(true, MutexName, out var first);
            if (!first)
            {
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(EventName);
                    signal.Set();
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                }
                return false;
            }
            var show = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            Background(() =>
            {
                while (show.WaitOne()) ShowRequested?.Invoke();
            });
            return true;
        }

        var endpoint = new System.Net.Sockets.UnixDomainSocketEndPoint(SocketPath);
        try
        {
            using var client = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
            client.Connect(endpoint);
            client.Send("show"u8);
            return false;
        }
        catch (System.Net.Sockets.SocketException)
        {
            // Nobody listening: this is the first one (a stale socket file is replaced).
        }
        try
        {
            File.Delete(SocketPath);
            listener = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
            listener.Bind(endpoint);
            listener.Listen(4);
            Background(() =>
            {
                while (true)
                {
                    using var connection = listener.Accept();
                    ShowRequested?.Invoke();
                }
            });
        }
        catch (Exception error) when (error is System.Net.Sockets.SocketException or IOException or UnauthorizedAccessException)
        {
            // Without the socket OpenParallel still works; a second start just opens a second window.
        }
        return true;
    }

    private static void Background(Action loop) =>
        new Thread(() =>
        {
            try
            {
                loop();
            }
            catch (Exception)
            {
            }
        }) { IsBackground = true, Name = "OpenParallel show requests" }.Start();
}

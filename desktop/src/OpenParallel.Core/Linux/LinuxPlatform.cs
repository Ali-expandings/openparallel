using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenParallel.Core.Linux;

/// <summary>Linux: XDG folders, desktop entries, <c>/proc</c>, signals, the freedesktop Trash
/// and X11 window activation. Nothing here needs root or extra packages.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class LinuxPlatform : IPlatform
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string DataHome { get; } =
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d : Path.Combine(Home, ".local", "share");

    public string Name => "linux";
    public IAppKnowledge Apps { get; } = new LinuxAppKnowledge();
    public string DataDirectory { get; } = Path.Combine(DataHome, "OpenParallel");
    public string ShortcutsFolder { get; } = Path.Combine(DataHome, "applications");
    public string DesktopDirectory { get; } = UserDirectory("DESKTOP") ?? Path.Combine(Home, "Desktop");
    public string LauncherPath { get; } = Path.Combine(AppContext.BaseDirectory, "OpenParallel");
    public ProcessMatching Matching => ProcessMatching.Arguments;
    public string ShortcutFileName(string instanceName, string slug) => $"openparallel-{slug}.desktop";
    public string ShortcutPattern => "openparallel-*.desktop";
    public string JoinArguments(IEnumerable<string> arguments) => PosixCommandLine.Join(arguments);
    public IReadOnlyList<string> SplitArguments(string text) => PosixCommandLine.Split(text);

    /// <summary>A folder from <c>~/.config/user-dirs.dirs</c> (e.g. <c>XDG_DESKTOP_DIR</c>).</summary>
    private static string? UserDirectory(string name)
    {
        try
        {
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(Home, ".config");
            var file = Path.Combine(config, "user-dirs.dirs");
            if (!File.Exists(file)) return null;
            foreach (var line in File.ReadLines(file))
            {
                if (!line.StartsWith($"XDG_{name}_DIR=", StringComparison.Ordinal)) continue;
                var value = line[(line.IndexOf('=') + 1)..].Trim('"').Replace("$HOME", Home);
                return Path.IsPathRooted(value) ? value : null;
            }
        }
        catch (IOException)
        {
        }
        return null;
    }

    // MARK: Shortcuts

    public void WriteShortcut(ShortcutDefinition shortcut)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcut.Path)!);
        File.WriteAllText(shortcut.Path, DesktopEntry.Write(shortcut));
        // Launchers on the desktop must be executable, and GNOME also wants them trusted.
        File.SetUnixFileMode(shortcut.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        if (Path.GetDirectoryName(shortcut.Path) == DesktopDirectory) Run("gio", "set", shortcut.Path, "metadata::trusted", "true");
    }

    public ShortcutDefinition? ReadShortcut(string path) => DesktopEntry.Load(path)?.ToShortcut();

    // MARK: Trash

    public void MoveToRecycleBin(IReadOnlyList<string> paths)
    {
        var trash = LinuxTrash.HomeTrash(DataHome);
        foreach (var path in paths) LinuxTrash.Move(path, trash);
    }

    // MARK: Processes

    /// <summary>Starts the app detached from OpenParallel's own input and output. A copy that kept
    /// them would hold opctl's output open for as long as it runs, so <c>opctl launch</c> in a
    /// script or a pipe never finished. <c>exec</c> keeps the process id the app's own.</summary>
    public int Start(LaunchPlan plan)
    {
        var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var argument in DetachedArguments(plan.Target, plan.Arguments)) info.ArgumentList.Add(argument);
        foreach (var (key, value) in plan.Environment) info.Environment[key] = value;
        if (plan.WorkingDirectory is { } dir && Directory.Exists(dir)) info.WorkingDirectory = dir;
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Couldn't start {plan.Target}.");
        return process.Id;
    }

    /// <summary><c>/bin/sh</c> arguments that run <paramref name="target"/> with stdin, stdout and
    /// stderr on /dev/null; the target and its arguments are passed as words, never parsed.</summary>
    public static IReadOnlyList<string> DetachedArguments(string target, IEnumerable<string> arguments) =>
        ["-c", "exec \"$0\" \"$@\" </dev/null >/dev/null 2>&1", target, .. arguments];

    public IReadOnlyList<ProcessSnapshot> Processes()
    {
        var uid = geteuid();
        var result = new List<ProcessSnapshot>();
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
            try
            {
                if (OwnerOf(pid) != uid) continue;
                var argv = File.ReadAllText($"{dir}/cmdline").Split('\0');
                var arguments = argv.Length > 0 && argv[^1].Length == 0 ? argv[..^1] : argv;
                if (arguments.Length == 0) continue; // kernel threads, zombies
                // Matching on Linux uses the exact arguments; the image path isn't needed.
                result.Add(new ProcessSnapshot(pid, arguments[0], string.Join(' ', arguments), arguments, ParentOf(pid)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Gone, or not readable.
            }
        }
        return result;
    }

    private static uint OwnerOf(int pid)
    {
        foreach (var line in File.ReadLines($"/proc/{pid}/status"))
        {
            if (line.StartsWith("Uid:", StringComparison.Ordinal)) return uint.Parse(line.Split('\t', StringSplitOptions.RemoveEmptyEntries)[1]);
        }
        return uint.MaxValue;
    }

    /// <summary>Field 4 of <c>/proc/pid/stat</c>, read after the closing parenthesis of the name.</summary>
    private static int ParentOf(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
        return int.TryParse(fields[1], out var parent) ? parent : 0;
    }

    public string? EnvironmentOf(int pid)
    {
        try
        {
            return File.ReadAllText($"/proc/{pid}/environ");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool IsRunning(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return stat[(stat.LastIndexOf(')') + 2)] != 'Z';
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>The process and everything it started (Chromium's windows belong to the main process).</summary>
    private IReadOnlySet<int> Family(int pid)
    {
        var processes = Processes();
        var result = new HashSet<int> { pid };
        bool added;
        do
        {
            added = false;
            foreach (var p in processes)
            {
                if (result.Contains(p.ParentPid) && result.Add(p.Pid)) added = true;
            }
        } while (added);
        return result;
    }

    public bool BringToFront(int pid) => X11.Activate(Family(pid));

    public bool RequestClose(int pid) => kill(pid, SIGTERM) == 0;

    public void ForceQuit(int pid)
    {
        foreach (var member in Family(pid)) kill(member, SIGKILL);
    }

    // MARK: Files

    /// <summary>Selects the item in the file manager (<c>org.freedesktop.FileManager1</c>), or
    /// opens its folder when no file manager answers.</summary>
    public void Reveal(string path)
    {
        var uri = new Uri(path).AbsoluteUri;
        if (Run("gdbus", "call", "--session", "--dest", "org.freedesktop.FileManager1", "--object-path", "/org/freedesktop/FileManager1",
                "--method", "org.freedesktop.FileManager1.ShowItems", $"['{uri.Replace("'", "%27")}']", "''")) return;
        Run("xdg-open", Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path);
    }

    public void OpenWithDefaultApp(string path) => Run("xdg-open", path);

    /// <summary>Runs a helper tool and waits briefly; false if it's missing or fails.</summary>
    private static bool Run(string tool, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            if (process is null) return false;
            if (!process.WaitForExit(3000)) return true; // still opening: that's fine
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private const int SIGTERM = 15;
    private const int SIGKILL = 9;

    [DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);
    [DllImport("libc")] private static extern uint geteuid();
}

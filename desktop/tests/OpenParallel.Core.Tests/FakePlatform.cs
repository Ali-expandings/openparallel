using OpenParallel.Core;

namespace OpenParallel.Core.Tests;

/// <summary>An in-memory Windows for tests on macOS. Shortcuts are written as small files so
/// existence checks behave; the Recycle Bin is a folder.</summary>
internal sealed class FakePlatform : IPlatform, IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "op-win-tests-" + Guid.NewGuid().ToString("N"));
    public string DataDirectory => Path.Combine(Root, "LocalAppData", "OpenParallel");
    /// <summary>A Linux-flavoured fake: desktop-entry names, argument matching, Linux recipes.</summary>
    public bool Linux { get; }
    public FakePlatform(bool linux)
        : this()
    {
        Linux = linux;
        Apps = linux ? new OpenParallel.Core.Linux.LinuxAppKnowledge() : new OpenParallel.Core.Windows.WindowsAppKnowledge();
    }

    public string Name => Linux ? "linux" : "windows";
    public IAppKnowledge Apps { get; } = new OpenParallel.Core.Windows.WindowsAppKnowledge();
    public string StartMenuPrograms => Path.Combine(Root, "StartMenu", "Programs");
    public string ShortcutsFolder => Path.Combine(StartMenuPrograms, "OpenParallel");
    public ProcessMatching Matching => Linux ? ProcessMatching.Arguments : ProcessMatching.ExecutableFolder;
    public string ShortcutFileName(string instanceName, string slug) => Linux ? $"openparallel-{slug}.desktop" : Naming.SafeFileName(instanceName) + ".lnk";
    public string ShortcutPattern => Linux ? "openparallel-*.desktop" : "*.lnk";
    public string JoinArguments(IEnumerable<string> arguments) =>
        Linux ? OpenParallel.Core.Linux.PosixCommandLine.Join(arguments) : WindowsCommandLine.Join(arguments);
    public IReadOnlyList<string> SplitArguments(string text) =>
        Linux ? OpenParallel.Core.Linux.PosixCommandLine.Split(text) : WindowsCommandLine.Split(text);
    public List<string> Revealed { get; } = [];
    public void Reveal(string path) => Revealed.Add(path);
    public void OpenWithDefaultApp(string path) => Revealed.Add(path);
    public string DesktopDirectory => Path.Combine(Root, "Desktop");
    public string LauncherPath => Path.Combine(Root, "OpenParallel", "OpenParallel.exe");
    public string RecycleBin => Path.Combine(Root, "RecycleBin");

    public Dictionary<string, ShortcutDefinition> Shortcuts { get; } = [];
    public List<LaunchPlan> Started { get; } = [];
    public List<ProcessSnapshot> RunningProcesses { get; } = [];
    public Dictionary<int, string> Environments { get; } = [];
    public List<int> ClosedPids { get; } = [];
    public bool FailShortcuts { get; set; }

    public FakePlatform() => Directory.CreateDirectory(Root);

    public void WriteShortcut(ShortcutDefinition shortcut)
    {
        if (FailShortcuts) throw new IOException("shortcut failed");
        Directory.CreateDirectory(Path.GetDirectoryName(shortcut.Path)!);
        File.WriteAllText(shortcut.Path, "lnk");
        Shortcuts[shortcut.Path] = shortcut;
    }

    public ShortcutDefinition? ReadShortcut(string path) => Shortcuts.GetValueOrDefault(path);

    public void MoveToRecycleBin(IReadOnlyList<string> paths)
    {
        Directory.CreateDirectory(RecycleBin);
        foreach (var path in paths)
        {
            var destination = Path.Combine(RecycleBin, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(path));
            if (Directory.Exists(path)) Directory.Move(path, destination);
            else if (File.Exists(path)) File.Move(path, destination);
        }
    }

    public int Start(LaunchPlan plan)
    {
        Started.Add(plan);
        return 1000 + Started.Count;
    }

    public IReadOnlyList<ProcessSnapshot> Processes() => RunningProcesses;
    public string? EnvironmentOf(int pid) => Environments.GetValueOrDefault(pid);
    public bool IsRunning(int pid) => RunningProcesses.Any(p => p.Pid == pid);
    public bool BringToFront(int pid) => IsRunning(pid);

    public bool RequestClose(int pid)
    {
        ClosedPids.Add(pid);
        return true;
    }

    public void ForceQuit(int pid) => RunningProcesses.RemoveAll(p => p.Pid == pid);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }

    /// <summary>Builds a fake installed app: files are created empty, folders as needed.</summary>
    public string MakeApp(string relativeExe, params string[] markers)
    {
        var exe = Path.Combine(Root, "Apps", relativeExe);
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "MZ");
        foreach (var marker in markers)
        {
            var path = Path.Combine(Path.GetDirectoryName(exe)!, marker);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");
        }
        return exe;
    }
}

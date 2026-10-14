namespace OpenParallel.Core;

/// <summary>How running processes are tied to instances.</summary>
public enum ProcessMatching
{
    /// <summary>Windows: the process runs from the app's folder and names the data folder.</summary>
    ExecutableFolder,
    /// <summary>Linux: the exact profile arguments (or recipe environment) appear in the process.</summary>
    Arguments,
}

/// <summary>Everything that touches the operating system. <see cref="Windows.WindowsPlatform"/>
/// and <see cref="Linux.LinuxPlatform"/> are the real things; tests on macOS use fakes.</summary>
public interface IPlatform
{
    /// <summary><c>windows</c> or <c>linux</c>; written into every manifest.</summary>
    string Name { get; }
    /// <summary>What this system's apps look like and how to copy them.</summary>
    IAppKnowledge Apps { get; }
    /// <summary>OpenParallel's own data folder (instances, logs).</summary>
    string DataDirectory { get; }
    /// <summary>Where instance shortcuts go (Start menu folder, applications folder).</summary>
    string ShortcutsFolder { get; }
    string DesktopDirectory { get; }
    /// <summary>Full path of the OpenParallel executable, for shortcuts that need the launcher.</summary>
    string LauncherPath { get; }
    ProcessMatching Matching { get; }

    /// <summary>File name of an instance's shortcut.</summary>
    string ShortcutFileName(string instanceName, string slug);
    /// <summary>Pattern of OpenParallel's shortcut files inside <see cref="ShortcutsFolder"/>.</summary>
    string ShortcutPattern { get; }

    /// <summary>Arguments as this system's shell writes them.</summary>
    string JoinArguments(IEnumerable<string> arguments);
    IReadOnlyList<string> SplitArguments(string text);

    void WriteShortcut(ShortcutDefinition shortcut);
    ShortcutDefinition? ReadShortcut(string path);

    /// <summary>Moves files and folders to the Recycle Bin / Trash (recoverable).</summary>
    void MoveToRecycleBin(IReadOnlyList<string> paths);

    /// <summary>Starts a process and returns its PID.</summary>
    int Start(LaunchPlan plan);

    IReadOnlyList<ProcessSnapshot> Processes();
    /// <summary>A process's environment as <c>KEY=VALUE</c> entries separated by NUL, or null if unreadable.</summary>
    string? EnvironmentOf(int pid);
    bool IsRunning(int pid);

    /// <summary>Restores and brings forward the process's main window.</summary>
    bool BringToFront(int pid);
    /// <summary>Asks the process to close (Windows: close its windows; Linux: SIGTERM).</summary>
    bool RequestClose(int pid);
    void ForceQuit(int pid);

    /// <summary>Shows the item in the file manager.</summary>
    void Reveal(string path);
    /// <summary>Opens a file with its default app.</summary>
    void OpenWithDefaultApp(string path);
}

/// <summary>Per-system knowledge of apps: what is installed, what an app is, and how to start
/// a copy of it with its own data.</summary>
public interface IAppKnowledge
{
    /// <summary>Inspects an exe (Windows) or a desktop entry or program (Linux).</summary>
    InspectedApp Inspect(string path);

    /// <summary>The launch plan for an app instance, and its launch mode
    /// (<c>direct</c>, <c>squirrel</c>, <c>launcher</c>, <c>flatpak</c>, <c>snap</c>…).</summary>
    (LaunchPlan Plan, string Mode) AppPlan(InspectedApp app, string dataRoot, bool separateData,
        IReadOnlyList<string> extraArguments, IReadOnlyDictionary<string, string> extraEnvironment, string slug, string launcherPath);

    /// <summary>A website instance: the browser as an app window with its own data.</summary>
    LaunchPlan WebPlan(InspectedApp browser, string url, string dataRoot, string slug);

    /// <summary>Folder the app's processes run from (Windows matching).</summary>
    string ProcessFolder(InspectedApp app);

    /// <summary>Where a recipe keeps data outside the instance folder, if anywhere (Snaps).</summary>
    string? ExtraDataPath(InspectedApp app, string slug);

    /// <summary>Writes the files a recipe needs before the first start (JetBrains properties).</summary>
    void Prepare(InspectedApp app, string dataRoot, bool separateData);

    IReadOnlyList<InstalledApp> InstalledApps(IPlatform platform);

    /// <summary>Chromium browsers that can open website instances, best first. <paramref name="known"/>
    /// are already-inspected apps (from the installed list) that may include more.</summary>
    IReadOnlyList<string> Browsers(IEnumerable<InspectedApp>? known = null);

    /// <summary>A file the user picked or dropped (shortcut, desktop entry, program) → the path to inspect.</summary>
    string? ResolvePicked(IPlatform platform, string path);
}

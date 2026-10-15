using System.Text.Json.Serialization;

namespace OpenParallel.Core;

/// <summary>Which recipe gives a copy its own data.</summary>
public enum AppFamily
{
    Generic,
    Chromium,
    Electron,
    VSCode,
    Firefox,
    Telegram,
    JetBrains,
    Packaged,
    Unsupported,
    Website,
}

/// <summary>How an app is installed, which decides what a shortcut points at.</summary>
public enum InstallKind
{
    /// <summary>An ordinary exe that stays where it is.</summary>
    Plain,
    /// <summary>Squirrel: <c>Update.exe</c> beside versioned <c>app-x.y.z</c> folders.</summary>
    Squirrel,
    /// <summary>Microsoft Store / MSIX package under <c>WindowsApps</c>.</summary>
    Packaged,
    /// <summary>Linux: a Flatpak app, started with <c>flatpak run</c>.</summary>
    Flatpak,
    /// <summary>Linux: a Snap, started from <c>/snap/bin</c>.</summary>
    Snap,
    /// <summary>Linux: a self-contained <c>.AppImage</c> file.</summary>
    AppImage,
}

/// <summary>What has actually been shown about an app. Never rounded up.</summary>
public enum CompatibilityStatus
{
    /// <summary>A real sign-in and sign-out test proved accounts stay apart.</summary>
    Verified,
    /// <summary>This exact version ran as two separate copies with separate data.</summary>
    Tested,
    /// <summary>A recipe exists for this kind of app; this version is untested.</summary>
    Supported,
    /// <summary>A best-effort recipe; test before relying on it.</summary>
    Experimental,
    /// <summary>Another copy can open but it uses the same data.</summary>
    SharesData,
    /// <summary>Windows or the app prevents a separate copy.</summary>
    NotSupported,
}

/// <summary>Everything OpenParallel learned about an installed app.</summary>
public sealed record InspectedApp
{
    public required string ExePath { get; init; }
    public required string Name { get; init; }
    public string Version { get; init; } = "";
    public string Company { get; init; } = "";
    public required AppFamily Family { get; init; }
    public InstallKind InstallKind { get; init; } = InstallKind.Plain;
    /// <summary>For Squirrel installs: the <c>Update.exe</c> that starts the newest version.</summary>
    public string? UpdateExePath { get; init; }
    /// <summary>For Squirrel installs: the folder holding <c>Update.exe</c> and the <c>app-*</c> folders.</summary>
    public string? InstallRoot { get; init; }
    public required CompatibilityStatus Status { get; init; }
    /// <summary>One plain sentence on what an instance of this app gets.</summary>
    public required string Notes { get; init; }
    /// <summary>For apps that cannot be copied: a website that can, if one exists.</summary>
    public string? WebAlternative { get; init; }
    /// <summary>Linux: the command that starts the app (from its desktop entry), without field codes.</summary>
    public IReadOnlyList<string> LaunchCommand { get; init; } = [];
    /// <summary>Linux: environment the desktop entry sets with <c>env K=V</c>.</summary>
    public IReadOnlyDictionary<string, string> LaunchEnvironment { get; init; } = new Dictionary<string, string>();
    /// <summary>Linux: the desktop entry's <c>Icon</c> (a theme name or a file).</summary>
    public string? IconName { get; init; }
    /// <summary>Linux: the Flatpak app id or Snap name. Windows: a Store app's AppUserModelId
    /// (<c>PackageFamilyName!AppId</c>).</summary>
    public string? PackageId { get; init; }
    /// <summary>Linux: the desktop entry's <c>Path</c>.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>True when a copy gets its own login and data.</summary>
    public bool SeparatesData => Status is CompatibilityStatus.Verified or CompatibilityStatus.Tested
        or CompatibilityStatus.Supported or CompatibilityStatus.Experimental;

    public bool CanCreate => Status != CompatibilityStatus.NotSupported;
}

/// <summary>What the user asked for in the New instance flow.</summary>
public sealed record InstanceRequest
{
    public required string Name { get; init; }
    /// <summary>The app's exe, or the browser exe for a website instance.</summary>
    public string? AppPath { get; init; }
    public string? WebUrl { get; init; }
    public bool SeparateData { get; init; } = true;
    public IReadOnlyList<string> ExtraArguments { get; init; } = [];
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public bool DesktopShortcut { get; init; }
}

/// <summary>Exactly how to start an instance.</summary>
public sealed record LaunchPlan
{
    public required string Target { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public string? WorkingDirectory { get; init; }
    /// <summary>Linux: the window class the recipe gives the app (<c>--class</c>), for the dock.</summary>
    public string? WindowClass { get; init; }

    public string CommandLine => WindowsCommandLine.Join(Arguments);
}

/// <summary>The <c>instance.json</c> written into every instance's data folder. Same core
/// fields as the Mac manifest, plus what Windows needs to launch it again.</summary>
public sealed record InstanceManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string Platform { get; init; } = "windows";
    public required string InstanceName { get; init; }
    public required string InstanceSlug { get; init; }
    /// <summary><c>app</c> or <c>website</c>.</summary>
    public required string Kind { get; init; }
    public required string Family { get; init; }
    /// <summary><c>direct</c>, <c>squirrel</c>, <c>launcher</c> or <c>web-app</c>.</summary>
    public required string LaunchMode { get; init; }
    public required string SourcePath { get; init; }
    public string SourceName { get; init; } = "";
    public string SourceVersionAtCreation { get; init; } = "";
    public bool ProfileIsolation { get; init; }
    public string? WebUrl { get; init; }
    public required string LaunchTarget { get; init; }
    public IReadOnlyList<string> LaunchArguments { get; init; } = [];
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public string? WorkingDirectory { get; init; }
    /// <summary>Folder whose executables belong to this instance (for running detection).</summary>
    public string? ProcessFolder { get; init; }
    public required string ShortcutPath { get; init; }
    public string? DesktopShortcutPath { get; init; }
    public required string AppUserModelId { get; init; }
    public required string CreatedAt { get; init; }
    /// <summary>Linux: icon for the shortcut (the source app's theme icon or file).</summary>
    public string? IconName { get; init; }
    /// <summary>Linux: window class given with <c>--class</c>, written as <c>StartupWMClass</c>.</summary>
    public string? WindowClass { get; init; }
    /// <summary>Linux: data kept outside the instance folder (Snaps can only write inside their own folder).</summary>
    public string? ExtraDataPath { get; init; }
    /// <summary>Websites: the browser it opens in, as the user knows it.</summary>
    public string? OpenedWith { get; init; }
    /// <summary>What OpenParallel said about the app when the instance was made, so its page says
    /// the same (a Snap, a Store app or an unknown Linux app is Experimental). Older instances have none.</summary>
    public CompatibilityStatus? StatusAtCreation { get; init; }
    public string? StatusNotes { get; init; }

    [JsonIgnore] public bool IsWebsite => Kind == "website";
}

/// <summary>A running process. <paramref name="Arguments"/> and <paramref name="ParentPid"/> are
/// filled on Linux, where the argument list is exact.</summary>
public sealed record ProcessSnapshot(int Pid, string ImagePath, string CommandLine, IReadOnlyList<string>? Arguments = null, int ParentPid = 0);

/// <summary>An app the user can pick in the New instance list. <paramref name="Path"/> is what
/// gets inspected: an exe on Windows, a desktop entry on Linux.</summary>
public sealed record InstalledApp(string Name, string Path)
{
    public string ExePath => Path;
}

/// <summary>A <c>.lnk</c> shortcut's contents.</summary>
public sealed record ShortcutDefinition
{
    public required string Path { get; init; }
    public required string Target { get; init; }
    /// <summary>Windows: the argument string. Linux uses <see cref="ArgumentList"/>.</summary>
    public string Arguments { get; init; } = "";
    public IReadOnlyList<string> ArgumentList { get; init; } = [];
    /// <summary>Linux: written into <c>Exec=env K=V …</c>.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public string? Name { get; init; }
    public string? WindowClass { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? IconPath { get; init; }
    public int IconIndex { get; init; }
    public string? Description { get; init; }
    public string? AppUserModelId { get; init; }
}

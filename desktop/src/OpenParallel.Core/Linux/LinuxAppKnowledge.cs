namespace OpenParallel.Core.Linux;

/// <summary>Linux apps: desktop entries, what kind of app each is, and how to start a copy with
/// its own data. Package-manager installs, AppImages, Flatpaks and Snaps differ only in the
/// wrapper around the same recipes.</summary>
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public sealed class LinuxAppKnowledge : IAppKnowledge
{
    private static readonly string[] ChromiumNames =
    [
        "chrome", "google-chrome", "google-chrome-stable", "google-chrome-beta", "google-chrome-unstable", "chromium",
        "chromium-browser", "microsoft-edge", "microsoft-edge-stable", "microsoft-edge-beta", "msedge", "brave",
        "brave-browser", "brave-browser-stable", "vivaldi", "vivaldi-stable", "opera", "thorium-browser", "ungoogled-chromium",
    ];
    private static readonly string[] ChromiumIds =
    [
        "org.chromium.chromium", "com.google.chrome", "com.microsoft.edge", "com.brave.browser", "com.vivaldi.vivaldi",
        "io.github.ungoogled_software.ungoogled_chromium", "com.opera.opera",
    ];
    private static readonly string[] VSCodeNames = ["code", "code-insiders", "codium", "vscodium", "cursor", "windsurf", "com.visualstudio.code", "com.vscodium.codium"];
    private static readonly string[] FirefoxNames =
    [
        "firefox", "firefox-esr", "firefox-developer-edition", "librewolf", "waterfox", "zen", "zen-browser", "floorp",
        "org.mozilla.firefox", "io.gitlab.librewolf-community", "app.zen_browser.zen", "one.ablaze.floorp",
        // Mozilla's mail apps take the same -profile and -no-remote.
        "thunderbird", "betterbird", "org.mozilla.thunderbird", "eu.betterbird.betterbird",
    ];
    private static readonly string[] TelegramNames = ["telegram-desktop", "telegram", "org.telegram.desktop"];
    private static readonly string[] ElectronNames =
    [
        "discord", "slack", "obsidian", "signal-desktop", "claude-desktop", "notion-app", "element-desktop", "teams-for-linux",
        "joplin", "bitwarden", "mattermost-desktop", "beeper", "whatsapp-for-linux", "com.discordapp.discord", "com.slack.slack",
        "md.obsidian.obsidian", "org.signal.signal", "im.riot.riot", "net.cozic.joplin_desktop", "com.bitwarden.desktop",
    ];
    private static readonly string[] JetBrainsScripts = ["idea", "pycharm", "webstorm", "phpstorm", "clion", "rider", "goland", "rubymine", "datagrip", "rustrover"];
    private static readonly Dictionary<string, string?> SingleInstance = new()
    {
        ["steam"] = null,
        ["com.valvesoftware.steam"] = null,
        ["zoom"] = "https://app.zoom.us",
        ["us.zoom.zoom"] = "https://app.zoom.us",
        ["spotify"] = "https://open.spotify.com",
        ["com.spotify.client"] = "https://open.spotify.com",
    };

    // MARK: Inspecting

    public InspectedApp Inspect(string path)
    {
        DesktopEntry? entry = path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? DesktopEntry.Load(path) : null;
        IReadOnlyList<string> command;
        IReadOnlyDictionary<string, string> entryEnvironment = new Dictionary<string, string>();
        if (entry is not null)
        {
            (command, entryEnvironment) = DesktopEntry.SplitEnvironment(DesktopEntry.SplitExec(entry["Exec"] ?? ""));
        }
        else
        {
            command = [path];
        }
        var name = entry?.Name ?? System.IO.Path.GetFileNameWithoutExtension(path);
        if (command.Count == 0)
        {
            return new InspectedApp
            {
                ExePath = path, Name = name, Family = AppFamily.Unsupported, Status = CompatibilityStatus.NotSupported,
                Notes = "This menu entry doesn't say how to start the app.",
            };
        }

        // How it is installed.
        var kind = InstallKind.Plain;
        string? packageId = null;
        var program = command[0];
        if (System.IO.Path.GetFileName(program) == "flatpak" && command.Contains("run"))
        {
            kind = InstallKind.Flatpak;
            packageId = FlatpakId(command) ?? entry?["X-Flatpak"];
        }
        else if (program.StartsWith("/snap/bin/", StringComparison.Ordinal) || entry?["X-SnapInstanceName"] is not null)
        {
            kind = InstallKind.Snap;
            packageId = entry?["X-SnapInstanceName"] ?? System.IO.Path.GetFileName(program).Split('.')[0];
        }
        else if (program.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase) || entry?["X-AppImage-Version"] is not null)
        {
            kind = InstallKind.AppImage;
        }

        var resolved = kind == InstallKind.Flatpak ? null : Resolve(program);
        var real = resolved is null ? null : RealPath(resolved);
        var folder = real is null ? null : System.IO.Path.GetDirectoryName(real);
        var names = new[]
        {
            System.IO.Path.GetFileName(program), resolved is null ? null : System.IO.Path.GetFileName(resolved),
            real is null ? null : System.IO.Path.GetFileName(real), packageId, entry?["StartupWMClass"],
            entry is null ? null : System.IO.Path.GetFileNameWithoutExtension(entry.Path),
        }
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!.ToLowerInvariant().Replace(".appimage", ""))
            .ToList();
        // AppImages are usually named App-1.2.3-x86_64.AppImage.
        if (kind == InstallKind.AppImage) names.AddRange(names.Select(n => n.Split('-', '_', '.')[0]).ToList());

        bool Named(string[] list) => names.Any(n => list.Contains(n));
        bool Has(params string[] relative) => folder is not null && relative.All(r => File.Exists(System.IO.Path.Combine(folder, r)));

        var single = names.FirstOrDefault(SingleInstance.ContainsKey);
        AppFamily family;
        if (single is not null) family = AppFamily.Unsupported;
        else if (Named(ChromiumNames) || Named(ChromiumIds) || Has("chrome_crashpad_handler") && Has("chrome_100_percent.pak") && !Has("resources/app.asar")) family = AppFamily.Chromium;
        else if (Named(VSCodeNames) || Has("resources/app/product.json")) family = AppFamily.VSCode;
        else if (Named(FirefoxNames) || Has("application.ini", "omni.ja")) family = AppFamily.Firefox;
        else if (Named(TelegramNames)) family = AppFamily.Telegram;
        else if (Named(ElectronNames) || Has("resources/app.asar") || Has("resources/app/package.json")) family = AppFamily.Electron;
        else if (names.Any(n => JetBrainsScripts.Any(s => n == s + ".sh")) || Has("idea.properties")) family = AppFamily.JetBrains;
        else family = AppFamily.Generic;

        CompatibilityStatus status;
        string notes;
        string? webAlternative = null;
        if (family == AppFamily.Unsupported)
        {
            status = CompatibilityStatus.NotSupported;
            webAlternative = SingleInstance[single!];
            notes = webAlternative is null
                ? $"{name} allows only one running copy on a computer."
                : $"{name} allows only one running copy on a computer. Create a website instance instead; each keeps its own login.";
        }
        else if (entry?.Flag("DBusActivatable") == true && family == AppFamily.Generic)
        {
            status = CompatibilityStatus.SharesData;
            notes = $"{name} hands every start to its running copy, so another copy opens in the original with the same data.";
        }
        else if (kind == InstallKind.Snap && family == AppFamily.Generic)
        {
            status = CompatibilityStatus.SharesData;
            notes = "Snap apps keep their data inside the snap, so OpenParallel can only open another copy with the same data.";
        }
        else if (family == AppFamily.Generic)
        {
            status = CompatibilityStatus.Experimental;
            notes = "Gets its own settings and data folders (XDG). Most Linux apps keep their data there; some use the original’s anyway. Test before relying on it.";
        }
        else if (family == AppFamily.JetBrains)
        {
            status = CompatibilityStatus.Experimental;
            notes = "Its settings, plugins and caches live in this instance's own folders. Test it before relying on it.";
        }
        else if (kind is InstallKind.Snap or InstallKind.Flatpak)
        {
            status = CompatibilityStatus.Experimental;
            notes = kind == InstallKind.Snap
                ? "A Snap: its data goes into the snap’s own folder. Test it before relying on it."
                : "A Flatpak: the sandbox is given just this instance’s folder. Test it before relying on it.";
        }
        else
        {
            status = CompatibilityStatus.Supported;
            notes = "OpenParallel knows how to give this kind of app its own data folder.";
        }

        return new InspectedApp
        {
            ExePath = path,
            Name = name,
            Version = entry?["X-AppImage-Version"] ?? "",
            Family = family,
            InstallKind = kind,
            Status = status,
            Notes = notes,
            WebAlternative = webAlternative,
            LaunchCommand = command,
            LaunchEnvironment = entryEnvironment,
            IconName = entry?["Icon"],
            PackageId = packageId,
            WorkingDirectory = entry?["Path"],
            InstallRoot = folder,
        };
    }

    /// <summary>The app id in <c>flatpak run [options] &lt;id&gt; [args]</c>.</summary>
    internal static string? FlatpakId(IReadOnlyList<string> command)
    {
        var run = command.ToList().IndexOf("run");
        for (var i = run + 1; i < command.Count; i++)
        {
            if (!command[i].StartsWith('-')) return command[i];
        }
        return null;
    }

    private static string? Resolve(string program)
    {
        if (program.Contains('/')) return File.Exists(program) ? program : null;
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "") + ":/usr/local/bin:/usr/bin:/bin:/snap/bin";
        foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = System.IO.Path.Combine(dir, program);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string RealPath(string path)
    {
        try
        {
            return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }
        catch (IOException)
        {
            return path;
        }
    }

    // MARK: Recipes

    public (LaunchPlan Plan, string Mode) AppPlan(InspectedApp app, string dataRoot, bool separateData,
        IReadOnlyList<string> extraArguments, IReadOnlyDictionary<string, string> extraEnvironment, string slug, string launcherPath)
    {
        var arguments = new List<string>();
        var environment = new Dictionary<string, string>(app.LaunchEnvironment);
        string? windowClass = null;
        if (separateData)
        {
            arguments.AddRange(RecipeBook.ProfileArguments(app.Family, dataRoot));
            if (app.Family is AppFamily.Chromium or AppFamily.Electron or AppFamily.Firefox)
            {
                windowClass = $"OpenParallel.{slug}";
                arguments.Add($"--class={windowClass}");
            }
            if (app.Family == AppFamily.JetBrains)
            {
                var script = System.IO.Path.GetFileName(app.LaunchCommand.FirstOrDefault() ?? app.ExePath);
                environment[$"{RecipeBook.JetBrainsProduct(script)}_PROPERTIES"] = System.IO.Path.Combine(dataRoot, "jetbrains", "idea.properties");
            }
            if (app.Family == AppFamily.Generic)
            {
                foreach (var (key, folder) in XdgFolders) environment[key] = System.IO.Path.Combine(dataRoot, "xdg", folder);
            }
        }
        arguments.AddRange(extraArguments);
        foreach (var (key, value) in extraEnvironment) environment[key] = value;
        var plan = Wrap(app, arguments, environment, dataRoot);
        var mode = app.InstallKind switch
        {
            InstallKind.Flatpak => "flatpak",
            InstallKind.Snap => "snap",
            InstallKind.AppImage => "appimage",
            _ => "direct",
        };
        return (plan with { WindowClass = windowClass }, mode);
    }

    private static readonly (string Key, string Folder)[] XdgFolders =
    [
        ("XDG_CONFIG_HOME", "config"), ("XDG_DATA_HOME", "data"), ("XDG_CACHE_HOME", "cache"), ("XDG_STATE_HOME", "state"),
    ];

    /// <summary>The app's own command with the recipe's arguments after it. A Flatpak gets
    /// <c>--filesystem</c> for the instance folder and the environment as <c>--env</c>, since the
    /// sandbox doesn't take the host's.</summary>
    private static LaunchPlan Wrap(InspectedApp app, List<string> arguments, Dictionary<string, string> environment, string dataRoot)
    {
        var command = app.LaunchCommand.Count > 0 ? app.LaunchCommand : [app.ExePath];
        if (app.InstallKind == InstallKind.Flatpak && app.PackageId is { } id && command.ToList().IndexOf(id) is var at and > 0)
        {
            var flatpakArgs = command.Skip(1).Take(at - 1).Where(a => a != "--file-forwarding").ToList();
            flatpakArgs.Add($"--filesystem={dataRoot}");
            flatpakArgs.AddRange(environment.Select(e => $"--env={e.Key}={e.Value}"));
            flatpakArgs.Add(id);
            flatpakArgs.AddRange(command.Skip(at + 1));
            flatpakArgs.AddRange(arguments);
            return new LaunchPlan { Target = command[0], Arguments = flatpakArgs, Environment = environment, WorkingDirectory = app.WorkingDirectory };
        }
        var target = Resolve(command[0]) ?? command[0];
        return new LaunchPlan
        {
            Target = target,
            Arguments = command.Skip(1).Concat(arguments).ToList(),
            Environment = environment,
            WorkingDirectory = app.WorkingDirectory,
        };
    }

    public LaunchPlan WebPlan(InspectedApp browser, string url, string dataRoot, string slug)
    {
        var windowClass = $"OpenParallel.{slug}";
        List<string> arguments =
        [
            $"--app={url}",
            $"--user-data-dir={System.IO.Path.Combine(dataRoot, "web")}",
            "--no-first-run",
            "--no-default-browser-check",
            $"--class={windowClass}",
        ];
        return Wrap(browser, arguments, new Dictionary<string, string>(browser.LaunchEnvironment), dataRoot) with { WindowClass = windowClass };
    }

    public string ProcessFolder(InspectedApp app) => app.InstallRoot ?? "";

    public string? ExtraDataPath(InspectedApp app, string slug) =>
        app.InstallKind == InstallKind.Snap && app.PackageId is { } snap
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "snap", snap, "common", "OpenParallel", slug)
            : null;

    public void Prepare(InspectedApp app, string dataRoot, bool separateData)
    {
        if (!separateData) return;
        Directory.CreateDirectory(dataRoot);
        foreach (var folder in RecipeBook.ProfileFolders(app.Family, dataRoot)) Directory.CreateDirectory(folder);
        if (app.Family == AppFamily.VSCode) RecipeBook.LeaveUpdatesToTheOriginal(dataRoot);
        if (app.Family == AppFamily.JetBrains)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(dataRoot, "jetbrains"));
            File.WriteAllText(System.IO.Path.Combine(dataRoot, "jetbrains", "idea.properties"), RecipeBook.JetBrainsProperties(dataRoot));
        }
        if (app.Family == AppFamily.Generic)
        {
            foreach (var (_, folder) in XdgFolders) Directory.CreateDirectory(System.IO.Path.Combine(dataRoot, "xdg", folder));
        }
    }

    // MARK: Installed apps

    /// <summary>Folders holding desktop entries, most personal first: the user's, then every
    /// <c>XDG_DATA_DIRS</c> entry, plus Flatpak's and Snap's exports when missing.</summary>
    public static IReadOnlyList<string> ApplicationFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d : System.IO.Path.Combine(home, ".local", "share");
        var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dirs ? dirs : "/usr/local/share:/usr/share";
        var roots = new List<string> { dataHome };
        roots.AddRange(dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries));
        roots.AddRange([System.IO.Path.Combine(dataHome, "flatpak", "exports", "share"), "/var/lib/flatpak/exports/share", "/var/lib/snapd/desktop"]);
        return roots.Select(r => System.IO.Path.Combine(r, "applications")).Distinct().ToList();
    }

    /// <summary>Settings panels, package managers, system monitors and help: the desktop's own
    /// tools, never something to sign in to twice (Windows leaves out Windows' own tools the same way).</summary>
    private static readonly string[] SystemCategories = ["Settings", "DesktopSettings", "HardwareSettings", "PackageManager", "Monitor", "Documentation"];

    /// <summary>Whether an entry belongs in the New instance list: an app the menu shows on this
    /// desktop (<c>OnlyShowIn</c>/<c>NotShowIn</c> against <c>XDG_CURRENT_DESKTOP</c>, as the
    /// desktop itself does), with a command, that isn't OpenParallel or a system tool.</summary>
    internal static bool Listed(DesktopEntry entry, IReadOnlyCollection<string> currentDesktops)
    {
        if (entry["Type"] != "Application" || entry.Flag("NoDisplay") || entry.Flag("Hidden") || entry.Flag("Terminal")) return false;
        if (entry["Exec"] is not { Length: > 0 } exec || exec.Contains("OpenParallel", StringComparison.Ordinal) || entry.Flag("X-OpenParallel")) return false;
        static string[] List(string? value) => (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Current(string desktop) => currentDesktops.Contains(desktop, StringComparer.OrdinalIgnoreCase);
        if (entry["OnlyShowIn"] is { } only && !List(only).Any(Current)) return false;
        if (List(entry["NotShowIn"]).Any(Current)) return false;
        return !List(entry["Categories"]).Any(c => SystemCategories.Contains(c, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyList<InstalledApp> InstalledApps(IPlatform platform)
    {
        var desktops = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<string>();
        var result = new List<InstalledApp>();
        foreach (var folder in ApplicationFolders().Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*.desktop", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
            }
            catch (IOException)
            {
                continue;
            }
            foreach (var file in files)
            {
                // The desktop file id: the path below applications/, with / as -.
                var id = System.IO.Path.GetRelativePath(folder, file).Replace('/', '-');
                if (!seen.Add(id) || id.StartsWith("openparallel", StringComparison.OrdinalIgnoreCase)) continue;
                var entry = DesktopEntry.Load(file);
                if (entry is null || !Listed(entry, desktops)) continue;
                if (entry["TryExec"] is { Length: > 0 } tryExec && Resolve(tryExec) is null) continue;
                result.Add(new InstalledApp(entry.Name, file));
            }
        }
        return result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static readonly string[] BrowserPreference = ["google-chrome", "chromium", "microsoft-edge", "brave", "vivaldi"];

    public IReadOnlyList<string> Browsers(IEnumerable<InspectedApp>? known = null)
    {
        var found = (known ?? []).Where(a => a.Family == AppFamily.Chromium).Select(a => a.ExePath).Distinct().ToList();
        if (found.Count == 0)
        {
            foreach (var name in new[] { "google-chrome-stable", "google-chrome", "chromium", "chromium-browser", "microsoft-edge-stable", "brave-browser", "vivaldi-stable" })
            {
                if (Resolve(name) is { } path && !found.Contains(path)) found.Add(path);
            }
        }
        return found
            .OrderBy(p => BrowserPreference.Select((b, i) => (b, i)).FirstOrDefault(x => System.IO.Path.GetFileName(p).Contains(x.b, StringComparison.OrdinalIgnoreCase), (b: "", i: 99)).i)
            .ToList();
    }

    public string? ResolvePicked(IPlatform platform, string path)
    {
        if (!File.Exists(path)) return null;
        if (path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)) return path;
        try
        {
            return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0 ? path : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

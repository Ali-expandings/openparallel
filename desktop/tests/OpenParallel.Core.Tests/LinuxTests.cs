using Xunit;
using OpenParallel.Core.Linux;

namespace OpenParallel.Core.Tests;

public class DesktopEntryTests
{
    [Fact]
    public void ParsesTheMainGroupOnlyAndSkipsLocalizedKeys()
    {
        var entry = DesktopEntry.Parse("""
            # comment
            [Desktop Entry]
            Type=Application
            Name=Slack
            Name[ar]=سلاك
            Exec=/usr/bin/slack %U
            Comment=Line one\nLine two
            [Desktop Action new]
            Name=Other
            """);
        Assert.Equal("Slack", entry.Name);
        Assert.Equal("Application", entry["Type"]);
        Assert.Equal("Line one\nLine two", entry["Comment"]);
        Assert.Null(entry["Name[ar]"]);
    }

    [Fact]
    public void SplitsExecWithQuotesEscapesAndFieldCodes()
    {
        Assert.Equal(["/opt/My App/app", "--flag", "say \"hi\" $HOME", "50%"],
            DesktopEntry.SplitExec("\"/opt/My App/app\" --flag \"say \\\"hi\\\" \\$HOME\" 50%% %U"));
        Assert.Equal(["/usr/bin/flatpak", "run", "--branch=stable", "--file-forwarding", "org.chromium.Chromium"],
            DesktopEntry.SplitExec("/usr/bin/flatpak run --branch=stable --file-forwarding org.chromium.Chromium @@u %U @@"));
    }

    [Fact]
    public void WrittenShortcutReadsBackExactly()
    {
        var shortcut = new ShortcutDefinition
        {
            Path = "/home/a/.local/share/applications/openparallel-x.desktop",
            Name = "Notes Copy",
            Target = "/opt/My App/app",
            ArgumentList = ["--user-data-dir=/home/a/b c/electron", "--x=$HOME", "100%", "back\\slash", "quote\"d"],
            Environment = new Dictionary<string, string> { ["XDG_CONFIG_HOME"] = "/home/a/b c/xdg/config" },
            WindowClass = "OpenParallel.x",
            IconPath = "notes",
            Description = "A separate copy",
        };
        var text = DesktopEntry.Write(shortcut);
        Assert.Contains("StartupWMClass=OpenParallel.x\n", text);
        Assert.Contains("Exec=env ", text);
        var back = DesktopEntry.Parse(text, shortcut.Path).ToShortcut();
        Assert.Equal(shortcut.Target, back.Target);
        Assert.Equal(shortcut.ArgumentList, back.ArgumentList);
        Assert.Equal(shortcut.Environment, back.Environment);
        Assert.Equal("Notes Copy", back.Name);
        Assert.Equal("notes", back.IconPath);
    }

    [Theory]
    [InlineData("--user-data-dir=/home/a/x")]
    [InlineData("two words")]
    [InlineData("it's")]
    [InlineData("a \"b\" $c \\d")]
    [InlineData("")]
    public void PosixQuotingRoundTrips(string argument)
    {
        Assert.Equal(["first", argument, "last"], PosixCommandLine.Split(PosixCommandLine.Join(["first", argument, "last"])));
    }
}

public class LinuxInspectorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "op-linux-" + Guid.NewGuid().ToString("N"));
    private readonly LinuxAppKnowledge apps = new();

    public LinuxInspectorTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    private string Program(string relative, params string[] markers)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "#!/bin/sh\n");
        foreach (var marker in markers)
        {
            var file = Path.Combine(Path.GetDirectoryName(path)!, marker);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "");
        }
        return path;
    }

    private string Entry(string name, string exec, params string[] extra)
    {
        var path = Path.Combine(root, "applications", name + ".desktop");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"[Desktop Entry]\nType=Application\nName={name}\nExec={exec}\nIcon={name.ToLowerInvariant()}\n" + string.Join("\n", extra) + "\n");
        return path;
    }

    [Fact]
    public void RecognisesFamiliesFromNamesAndFolders()
    {
        Assert.Equal(AppFamily.Chromium, apps.Inspect(Entry("Chromium", Program("bin/chromium") + " %U")).Family);
        Assert.Equal(AppFamily.Electron, apps.Inspect(Entry("Notes", Program("opt/notes/notes", "resources/app.asar"))).Family);
        Assert.Equal(AppFamily.VSCode, apps.Inspect(Entry("Editor", Program("opt/ed/ed", "resources/app/product.json"))).Family);
        Assert.Equal(AppFamily.Firefox, apps.Inspect(Entry("Fox", Program("lib/fox/fox", "application.ini", "omni.ja"))).Family);
        Assert.Equal(AppFamily.Telegram, apps.Inspect(Entry("Telegram", Program("bin/telegram-desktop") + " -- %u")).Family);
        var jetbrains = apps.Inspect(Entry("PyCharm", Program("opt/pycharm/bin/pycharm.sh")));
        Assert.Equal(AppFamily.JetBrains, jetbrains.Family);
        Assert.Equal(CompatibilityStatus.Experimental, jetbrains.Status);
        var supported = apps.Inspect(Entry("Chromium", Program("bin/chromium")));
        Assert.Equal(CompatibilityStatus.Supported, supported.Status);
        Assert.Equal("chromium", supported.IconName);
    }

    [Fact]
    public void ElectronAppsAreNotTakenForBrowsers()
    {
        // VS Code 1.142 for Linux ARM: Chromium's files beside resources/app/product.json (2026-10-16).
        string[] chromiumFiles = ["chrome_crashpad_handler", "chrome_100_percent.pak"];
        var vscode = apps.Inspect(Entry("Visual Studio Code", Program("op-apps/VSCode-linux-arm64/code", [.. chromiumFiles, "resources/app/product.json"]) + " %F"));
        Assert.Equal(AppFamily.VSCode, vscode.Family);
        Assert.Equal(AppFamily.Electron, apps.Inspect(Entry("Chat", Program("opt/chat/chat", [.. chromiumFiles, "resources/app.asar"]))).Family);
        Assert.Equal(AppFamily.Chromium, apps.Inspect(Entry("Some Browser", Program("opt/browser/browser", chromiumFiles))).Family);
    }

    [Fact]
    public void ChatGptCopiesGetTheirOwnCodexHome()
    {
        // ChatGPT 26.1013 for Linux: /usr/bin/chatgpt runs a launcher beside the app and resources/codex.
        var entry = Entry("ChatGPT", Program("usr/lib/chatgpt/codex-launcher", "ChatGPT", "resources/app.asar", "resources/codex") + " %U");
        var app = apps.Inspect(entry);
        Assert.Equal(AppFamily.Electron, app.Family);
        Assert.Contains("CODEX_HOME", app.Notes);
        var (plan, _) = apps.AppPlan(app, "/data/chatgpt-copy", true, [], new Dictionary<string, string>(), "chatgpt-copy", "/op");
        Assert.Equal(Path.Combine("/data/chatgpt-copy", "codex-home"), plan.Environment["CODEX_HOME"]);
        Assert.Contains("--user-data-dir=" + Path.Combine("/data/chatgpt-copy", "electron"), plan.Arguments);

        var plain = apps.Inspect(Entry("Notes", Program("opt/notes/notes", "resources/app.asar")));
        Assert.False(apps.AppPlan(plain, "/data/n", true, [], new Dictionary<string, string>(), "n", "/op").Plan.Environment.ContainsKey("CODEX_HOME"));
    }

    [Fact]
    public void AnyOtherAppGetsItsOwnXdgFolders()
    {
        var tool = apps.Inspect(Entry("Tool", "env GDK_BACKEND=x11 " + Program("bin/tool")));
        Assert.Equal(AppFamily.Generic, tool.Family);
        Assert.Equal(CompatibilityStatus.Experimental, tool.Status);
        Assert.True(tool.SeparatesData);
        Assert.Equal("x11", tool.LaunchEnvironment["GDK_BACKEND"]);
        var (plan, mode) = apps.AppPlan(tool, "/data/tool", true, ["--verbose"], new Dictionary<string, string>(), "tool", "/op");
        Assert.Equal("direct", mode);
        Assert.Equal("/data/tool/xdg/config", plan.Environment["XDG_CONFIG_HOME"]);
        Assert.Equal("/data/tool/xdg/cache", plan.Environment["XDG_CACHE_HOME"]);
        Assert.Equal("x11", plan.Environment["GDK_BACKEND"]);
        Assert.Equal(["--verbose"], plan.Arguments);
    }

    [Fact]
    public void DbusActivatedAndSingleInstanceAppsAreHonest()
    {
        var dbus = apps.Inspect(Entry("Editor", Program("bin/gedit2"), "DBusActivatable=true"));
        Assert.Equal(CompatibilityStatus.SharesData, dbus.Status);
        var zoom = apps.Inspect(Entry("Zoom", Program("bin/zoom") + " %U"));
        Assert.Equal(CompatibilityStatus.NotSupported, zoom.Status);
        Assert.Equal("https://app.zoom.us", zoom.WebAlternative);
        Assert.False(zoom.CanCreate);
    }

    [Fact]
    public void ChromiumGetsItsOwnProfileAndWindowClass()
    {
        var chromium = apps.Inspect(Entry("Chromium", Program("bin/chromium") + " %U"));
        var (plan, _) = apps.AppPlan(chromium, "/data/c", true, [], new Dictionary<string, string>(), "c", "/op");
        Assert.Equal(["--user-data-dir=/data/c/chromium", "--class=OpenParallel.c"], plan.Arguments);
        Assert.Equal("OpenParallel.c", plan.WindowClass);
    }

    [Fact]
    public void FlatpaksRunWithAccessToJustTheInstanceFolder()
    {
        var app = apps.Inspect(Entry("Chromium Flatpak",
            "/usr/bin/flatpak run --branch=stable --arch=aarch64 --command=chromium --file-forwarding org.chromium.Chromium @@u %U @@",
            "X-Flatpak=org.chromium.Chromium"));
        Assert.Equal(InstallKind.Flatpak, app.InstallKind);
        Assert.Equal(AppFamily.Chromium, app.Family);
        Assert.Equal("org.chromium.Chromium", app.PackageId);
        var (plan, mode) = apps.AppPlan(app, "/data/f", true, ["--x"], new Dictionary<string, string> { ["A"] = "1" }, "f", "/op");
        Assert.Equal("flatpak", mode);
        Assert.Equal("/usr/bin/flatpak", plan.Target);
        Assert.Equal(["run", "--branch=stable", "--arch=aarch64", "--command=chromium", "--filesystem=/data/f", "--env=A=1",
            "org.chromium.Chromium", "--user-data-dir=/data/f/chromium", "--class=OpenParallel.f", "--x"], plan.Arguments);
    }

    [Fact]
    public void SnapsKeepTheirDataInsideTheSnap()
    {
        var app = apps.Inspect(Entry("Firefox", "/snap/bin/firefox %u", "X-SnapInstanceName=firefox"));
        Assert.Equal(InstallKind.Snap, app.InstallKind);
        Assert.Equal(AppFamily.Firefox, app.Family);
        Assert.Equal(CompatibilityStatus.Experimental, app.Status);
        var extra = apps.ExtraDataPath(app, "ff");
        Assert.EndsWith("/snap/firefox/common/OpenParallel/ff", extra);
        var generic = apps.Inspect(Entry("Notes", "/snap/bin/notes", "X-SnapInstanceName=notes"));
        Assert.Equal(CompatibilityStatus.SharesData, generic.Status);
    }

    [Fact]
    public void WebsitesUseTheBrowserAsAnAppWindow()
    {
        var browser = apps.Inspect(Entry("Chromium", Program("bin/chromium") + " %U"));
        var plan = apps.WebPlan(browser, "https://web.whatsapp.com", "/data/w", "w");
        Assert.Equal(["--app=https://web.whatsapp.com", "--user-data-dir=/data/w/web", "--no-first-run", "--no-default-browser-check",
            "--class=OpenParallel.w"], plan.Arguments);
        Assert.Equal("OpenParallel.w", plan.WindowClass);
    }
}

public class LinuxMatcherTests
{
    private static InstanceManifest Manifest(string slug, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? env = null) => new()
    {
        InstanceName = slug, InstanceSlug = slug, Kind = "app", Family = "Chromium", LaunchMode = "direct", SourcePath = "/x",
        LaunchTarget = "/usr/bin/chromium", LaunchArguments = args, Environment = env ?? new Dictionary<string, string>(),
        ProfileIsolation = true, ShortcutPath = "/s", AppUserModelId = "", CreatedAt = "", Platform = "linux",
    };

    private static string Root(InstanceManifest m) => "/home/a/.local/share/OpenParallel/Instances/" + m.InstanceSlug;

    [Fact]
    public void MatchesTheExactProfileArgumentsAndSkipsHelpersAndFileManagers()
    {
        var a = Manifest("work", ["--user-data-dir=/home/a/.local/share/OpenParallel/Instances/work/chromium", "--class=OpenParallel.work"]);
        var b = Manifest("work-2", ["--user-data-dir=/home/a/.local/share/OpenParallel/Instances/work-2/chromium"]);
        var processes = new List<ProcessSnapshot>
        {
            new(10, "/usr/lib/chromium/chromium", "", ["/usr/lib/chromium/chromium", "--user-data-dir=/home/a/.local/share/OpenParallel/Instances/work/chromium", "--class=OpenParallel.work"], 1),
            new(11, "/usr/lib/chromium/chromium", "", ["/usr/lib/chromium/chromium", "--type=renderer", "--user-data-dir=/home/a/.local/share/OpenParallel/Instances/work/chromium"], 10),
            new(12, "/usr/bin/nautilus", "", ["nautilus", "/home/a/.local/share/OpenParallel/Instances/work-2/chromium"], 1),
            new(13, "/usr/lib/chromium/chromium", "", ["/usr/lib/chromium/chromium", "--user-data-dir=/home/a/.local/share/OpenParallel/Instances/work-2/chromium"], 1),
        };
        var running = ProcessMatcher.MatchArguments([a, b], Root, processes, _ => null);
        Assert.Equal([10], running["work"]);
        Assert.Equal([13], running["work-2"]);
    }

    [Fact]
    public void AppsThatRewriteTheirProcessTitleAreStillRecognised()
    {
        // Claude 2.42304 on Ubuntu 24.04: /proc/<pid>/cmdline is one string of the words joined by spaces.
        var claude = Manifest("claude-copy", ["--user-data-dir=/home/a/.local/share/OpenParallel/Instances/claude-copy/electron", "--class=OpenParallel.claude-copy"]);
        var other = Manifest("claude-copy-2", ["--user-data-dir=/home/a/.local/share/OpenParallel/Instances/claude-copy-2/electron"]);
        var processes = new List<ProcessSnapshot>
        {
            new(30, "/usr/lib/claude-desktop/claude-desktop", "", ["/usr/lib/claude-desktop/claude-desktop"], 1),
            new(31, "/usr/lib/claude-desktop/claude-desktop", "",
                ["/usr/lib/claude-desktop/claude-desktop --user-data-dir=/home/a/.local/share/OpenParallel/Instances/claude-copy/electron --class=OpenParallel.claude-copy"], 1),
            new(32, "/usr/lib/claude-desktop/claude-desktop", "",
                ["/usr/lib/claude-desktop/claude-desktop --type=renderer --user-data-dir=/home/a/.local/share/OpenParallel/Instances/claude-copy/electron"], 31),
        };
        var running = ProcessMatcher.MatchArguments([claude, other], Root, processes, _ => null);
        Assert.Equal([31], running["claude-copy"]);
        // A word is matched whole: claude-copy's folder is not claude-copy-2's.
        Assert.False(running.ContainsKey("claude-copy-2"));
    }

    [Fact]
    public void EnvironmentRecipesMatchTheTopmostProcessOnly()
    {
        var env = new Dictionary<string, string> { ["XDG_CONFIG_HOME"] = "/home/a/.local/share/OpenParallel/Instances/tool/xdg/config" };
        var tool = Manifest("tool", [], env);
        var processes = new List<ProcessSnapshot>
        {
            new(20, "/usr/bin/tool", "", ["/usr/bin/tool"], 1),
            new(21, "/usr/bin/tool-helper", "", ["/usr/bin/tool-helper"], 20),
            new(22, "/usr/bin/tool", "", ["/usr/bin/tool"], 1),
        };
        string? Environment(int pid) => pid is 20 or 21
            ? "HOME=/home/a\0XDG_CONFIG_HOME=/home/a/.local/share/OpenParallel/Instances/tool/xdg/config\0"
            : "HOME=/home/a\0";
        var running = ProcessMatcher.MatchArguments([tool], Root, processes, Environment);
        Assert.Equal([20], running["tool"]);
    }
}

public class LinuxTrashTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "op-trash-" + Guid.NewGuid().ToString("N"));
    public LinuxTrashTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void MovesItemsAndRecordsWhereTheyCameFrom()
    {
        var trash = Path.Combine(root, "Trash");
        var folder = Path.Combine(root, "My Data");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.txt"), "x");
        var moved = LinuxTrash.Move(folder, trash, new DateTime(2026, 10, 14, 12, 0, 0));
        Assert.False(Directory.Exists(folder));
        Assert.True(File.Exists(Path.Combine(moved, "a.txt")));
        var info = File.ReadAllText(Path.Combine(trash, "info", "My Data.trashinfo"));
        Assert.Contains("Path=" + LinuxTrash.EncodePath(folder) + "\n", info);
        Assert.Contains("%20Data", info);
        Assert.Contains("DeletionDate=2026-10-14T12:00:00", info);

        Directory.CreateDirectory(folder);
        var second = LinuxTrash.Move(folder, trash);
        Assert.EndsWith("My Data.2", second);
    }
}

public class LinuxServiceTests
{
    [Fact]
    public void CreatesADesktopEntryAndRecognisesTheRunningCopy()
    {
        using var fake = new FakePlatform(linux: true);
        var service = new InstanceService(fake);
        var program = fake.MakeApp("tool/tool");
        var entry = Path.Combine(fake.Root, "tool.desktop");
        File.WriteAllText(entry, $"[Desktop Entry]\nType=Application\nName=Tool\nExec={program} %F\nIcon=tool\n");

        var manifest = service.Create(new InstanceRequest { Name = "Tool Copy", AppPath = entry });

        Assert.Equal("linux", manifest.Platform);
        Assert.EndsWith("openparallel-tool-copy.desktop", manifest.ShortcutPath);
        Assert.Equal("tool", manifest.IconName);
        Assert.True(manifest.ProfileIsolation);
        var config = manifest.Environment["XDG_CONFIG_HOME"];
        Assert.StartsWith(service.DataRootOf(manifest), config);
        Assert.True(Directory.Exists(config));
        Assert.Equal(fake.Shortcuts[manifest.ShortcutPath].Environment, manifest.Environment);
        Assert.Single(service.List());

        fake.RunningProcesses.Add(new ProcessSnapshot(500, program, program, [program], 1));
        fake.Environments[500] = $"XDG_CONFIG_HOME={config}\0XDG_DATA_HOME={manifest.Environment["XDG_DATA_HOME"]}\0" +
                                 $"XDG_CACHE_HOME={manifest.Environment["XDG_CACHE_HOME"]}\0XDG_STATE_HOME={manifest.Environment["XDG_STATE_HOME"]}\0";
        Assert.Equal([500], service.RunningPids(manifest));
        Assert.Throws<InvalidOperationException>(() => service.MoveToRecycleBin(manifest));

        fake.RunningProcesses.Clear();
        service.MoveToRecycleBin(manifest);
        Assert.Empty(service.List());
        Assert.False(File.Exists(manifest.ShortcutPath));
    }
}

/// <summary>Found on Ubuntu 24.04 (2026-10-15): a started copy kept opctl's output open, so
/// <c>opctl launch</c> in a script never returned while the copy ran.</summary>
public class DetachedStartTests
{
    private static System.Diagnostics.Process Start(string target, params string[] arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo("/bin/sh") { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var argument in LinuxPlatform.DetachedArguments(target, arguments)) info.ArgumentList.Add(argument);
        return System.Diagnostics.Process.Start(info)!;
    }

    [Fact]
    public void TheCallersOutputClosesWhileTheAppKeepsRunning()
    {
        using var app = Start("/bin/sleep", "5");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        app.StandardOutput.ReadToEnd();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"output stayed open {watch.Elapsed}");
        Assert.False(app.HasExited);
        app.Kill();
    }

    [Fact]
    public void ArgumentsArriveAsWordsAndTheProcessIdIsTheAppsOwn()
    {
        var output = Path.Combine(Path.GetTempPath(), $"op-detached-{Guid.NewGuid():N}.txt");
        try
        {
            using var app = Start("/bin/sh", "-c", "printf '%s|' \"$@\" > \"$0\"; printf '\\n%s\\n' $$ >> \"$0\"", output, "a b", "c\"d $HOME");
            app.WaitForExit(5000);
            var lines = File.ReadAllLines(output);
            Assert.Equal("a b|c\"d $HOME|", lines[0]);
            Assert.Equal(app.Id.ToString(), lines[1]);
        }
        finally
        {
            File.Delete(output);
        }
    }
}

/// <summary>Found on Ubuntu 24.04 (2026-10-15).</summary>
public class UbuntuFindingsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"op-ubuntu-{Guid.NewGuid():N}");

    public UbuntuFindingsTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static DesktopEntry Entry(string lines) => DesktopEntry.Parse("[Desktop Entry]\nType=Application\nName=X\nExec=/usr/bin/x\n" + lines);

    [Fact]
    public void TheListFollowsTheDesktopsRulesAndLeavesOutSystemTools()
    {
        string[] gnome = ["ubuntu", "GNOME"];
        Assert.True(LinuxAppKnowledge.Listed(Entry("Categories=Office;Spreadsheet;"), gnome));
        Assert.True(LinuxAppKnowledge.Listed(Entry("OnlyShowIn=GNOME;Unity;\nCategories=GNOME;GTK;Utility;"), gnome));
        // gnome-system-monitor-kde.desktop, Additional Drivers (NotShowIn=KDE is fine here, Settings isn't).
        Assert.False(LinuxAppKnowledge.Listed(Entry("OnlyShowIn=KDE;\nCategories=GNOME;GTK;System;"), gnome));
        Assert.False(LinuxAppKnowledge.Listed(Entry("NotShowIn=ubuntu;"), gnome));
        Assert.False(LinuxAppKnowledge.Listed(Entry("NotShowIn=KDE;\nCategories=X-XFCE;GTK;Settings;HardwareSettings"), gnome));
        Assert.False(LinuxAppKnowledge.Listed(Entry("Categories=GNOME;GTK;System;PackageManager;"), gnome));
        Assert.False(LinuxAppKnowledge.Listed(Entry("Categories=GNOME;GTK;System;Monitor;"), gnome));
        Assert.False(LinuxAppKnowledge.Listed(Entry("Categories=GNOME;GTK;Core;Documentation;Utility;"), gnome));
        Assert.False(LinuxAppKnowledge.Listed(Entry("NoDisplay=true"), gnome));
        // With no desktop known, an entry for one desktop only is left out, as GLib does.
        Assert.False(LinuxAppKnowledge.Listed(Entry("OnlyShowIn=GNOME;"), []));
    }

    [Fact]
    public void ThunderbirdFromASnapIsAFirefoxFamilyApp()
    {
        var entry = Path.Combine(root, "thunderbird_thunderbird.desktop");
        File.WriteAllText(entry, "[Desktop Entry]\nType=Application\nName=Thunderbird Mail\nExec=/snap/bin/thunderbird %u\nX-SnapInstanceName=thunderbird\n");
        var app = new LinuxAppKnowledge().Inspect(entry);
        Assert.Equal(AppFamily.Firefox, app.Family);
        Assert.Equal(InstallKind.Snap, app.InstallKind);
        Assert.True(app.SeparatesData);
    }

    [Fact]
    public void IconsComeFromTheThemeAndWhatItInherits()
    {
        Directory.CreateDirectory(Path.Combine(root, "icons", "Yaru"));
        File.WriteAllText(Path.Combine(root, "icons", "Yaru", "index.theme"), "[Icon Theme]\nName=Yaru\nInherits=Humanity,hicolor\n");
        Directory.CreateDirectory(Path.Combine(root, "icons", "Humanity"));
        File.WriteAllText(Path.Combine(root, "icons", "Humanity", "index.theme"), "[Icon Theme]\nInherits=Adwaita,hicolor\n");
        Assert.Equal(["Yaru", "Humanity", "Adwaita", "hicolor"], LinuxIcons.ThemeChain("Yaru", [root]));
        Assert.Equal(["hicolor"], LinuxIcons.ThemeChain(null, [root]));
    }

    [Fact]
    public void AFirefoxCopysProfileFolderIsMadeBeforeItStarts()
    {
        var data = Path.Combine(root, "snap", "firefox", "common", "OpenParallel", "fox-copy");
        var app = new InspectedApp { ExePath = "/x.desktop", Name = "Firefox", Family = AppFamily.Firefox, Status = CompatibilityStatus.Experimental, Notes = "" };
        new LinuxAppKnowledge().Prepare(app, data, separateData: true);
        Assert.True(Directory.Exists(Path.Combine(data, "firefox")));
        Assert.Contains(Path.Combine(data, "firefox"), RecipeBook.ProfileArguments(AppFamily.Firefox, data));
    }
}

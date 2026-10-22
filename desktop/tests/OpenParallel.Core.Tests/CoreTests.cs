using OpenParallel.Core;
using Xunit;

namespace OpenParallel.Core.Tests;

public class NamingTests
{
    [Theory]
    [InlineData("Claude Copy", "claude-copy")]
    [InlineData("Claude Copy 2", "claude-copy-2")]
    [InlineData("  --Work__Profile--  ", "work-profile")]
    [InlineData("Ünïcode", "ufc-n-uef-code")]
    [InlineData("!!!", "shortcut")]
    public void SlugMatchesTheMacRule(string name, string slug) => Assert.Equal(slug, Naming.Slug(name));

    [Fact]
    public void SafeFileNameRemovesCharactersWindowsRejects() =>
        Assert.Equal("Work  Profile", Naming.SafeFileName("Work: Profile?"));
}

public class CommandLineTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("has space", "\"has space\"")]
    [InlineData(@"C:\Program Files\App\", "\"C:\\Program Files\\App\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    public void QuotesLikeCommandLineToArgvExpects(string argument, string quoted) =>
        Assert.Equal(quoted, WindowsCommandLine.Quote(argument));

    [Fact]
    public void JoinAndSplitRoundTrip()
    {
        string[] args = ["--user-data-dir=C:\\Users\\me\\AppData\\Local\\OpenParallel\\Instances\\claude-copy\\electron",
            "", "two words", "trailing\\", "quote\"inside", "back\\\\slashes\\\"quote", "-profile", "C:\\a b\\c"];
        Assert.Equal(args, WindowsCommandLine.Split(WindowsCommandLine.Join(args)));
    }
}

public class InspectorTests
{
    [Fact]
    public void DetectsEachFamilyFromFilesBesideTheExe()
    {
        using var fake = new FakePlatform();
        Assert.Equal(AppFamily.Chromium, AppInspector.Inspect(fake.MakeApp(@"Chrome/Application/chrome.exe", "131.0.1.2/chrome_elf.dll")).Family);
        Assert.Equal(AppFamily.Chromium, AppInspector.Inspect(fake.MakeApp(@"Odd/Application/odd.exe", "1.0/chrome_elf.dll")).Family);
        Assert.Equal(AppFamily.Electron, AppInspector.Inspect(fake.MakeApp(@"Notion/Notion.exe", "resources/app.asar")).Family);
        Assert.Equal(AppFamily.VSCode, AppInspector.Inspect(fake.MakeApp(@"Code/Code.exe", "resources/app/product.json", "resources/app/package.json")).Family);
        Assert.Equal(AppFamily.Firefox, AppInspector.Inspect(fake.MakeApp(@"Fox/fox.exe", "application.ini", "omni.ja")).Family);
        Assert.Equal(AppFamily.Telegram, AppInspector.Inspect(fake.MakeApp(@"Telegram Desktop/Telegram.exe")).Family);
        Assert.Equal(AppFamily.JetBrains, AppInspector.Inspect(fake.MakeApp(@"IDEA/bin/idea64.exe", "idea.properties")).Family);
        var generic = AppInspector.Inspect(fake.MakeApp(@"Tool/tool.exe"));
        Assert.Equal(AppFamily.Generic, generic.Family);
        Assert.Equal(CompatibilityStatus.SharesData, generic.Status);
        Assert.False(generic.SeparatesData);
    }

    [Fact]
    public void SingleInstanceAppsAreRefusedWithAWebsiteAlternative()
    {
        using var fake = new FakePlatform();
        var zoom = AppInspector.Inspect(fake.MakeApp(@"Zoom/bin/Zoom.exe"));
        Assert.Equal(CompatibilityStatus.NotSupported, zoom.Status);
        Assert.False(zoom.CanCreate);
        Assert.Equal("https://app.zoom.us", zoom.WebAlternative);
        Assert.Equal(CompatibilityStatus.NotSupported, AppInspector.Inspect(@"C:\Program Files\WindowsApps\Claude_1.0_x64__pzs8sxrjxfjjc\app\claude.exe").Status);
    }

    [Fact]
    public void SquirrelInstallsLaunchTheNewestVersionThroughUpdateExe()
    {
        using var fake = new FakePlatform();
        fake.MakeApp(@"AnthropicClaude/Update.exe");
        fake.MakeApp(@"AnthropicClaude/app-1.2.0/claude.exe", "resources/app.asar");
        var newest = fake.MakeApp(@"AnthropicClaude/app-1.10.0/claude.exe", "resources/app.asar");
        var stub = fake.MakeApp(@"AnthropicClaude/claude.exe");

        foreach (var exe in new[] { newest, stub })
        {
            var app = AppInspector.Inspect(exe);
            Assert.Equal(InstallKind.Squirrel, app.InstallKind);
            Assert.Equal(AppFamily.Electron, app.Family);
            Assert.EndsWith("Update.exe", app.UpdateExePath);
        }
        Assert.EndsWith("app-1.10.0", AppInspector.NewestAppFolder(Path.GetDirectoryName(stub)!));
    }
}

public class RecipeTests
{
    private static InspectedApp App(AppFamily family, string exe = @"C:\Apps\app.exe") => new()
    {
        ExePath = exe, Name = "App", Family = family, Status = CompatibilityStatus.Supported, Notes = "",
    };

    [Fact]
    public void ChromiumGetsItsOwnUserDataFolder()
    {
        var (plan, mode) = RecipeBook.AppPlan(App(AppFamily.Chromium), "/data/x", true, [], new Dictionary<string, string>(), "x", "/op.exe");
        Assert.Equal("direct", mode);
        Assert.Equal(["--user-data-dir=" + Path.Combine("/data/x", "chromium")], plan.Arguments);
    }

    [Fact]
    public void SharedDataAddsNoProfileSwitches()
    {
        var (plan, _) = RecipeBook.AppPlan(App(AppFamily.Electron), "/data/x", false, ["--flag"], new Dictionary<string, string>(), "x", "/op.exe");
        Assert.Equal(["--flag"], plan.Arguments);
    }

    [Fact]
    public void SquirrelWrapsArgumentsForUpdateExe()
    {
        var app = App(AppFamily.Electron) with
        {
            InstallKind = InstallKind.Squirrel, UpdateExePath = "/u/AnthropicClaude/Update.exe",
            InstallRoot = "/u/AnthropicClaude", ExePath = "/u/AnthropicClaude/app-1.0.0/claude.exe",
        };
        var (plan, mode) = RecipeBook.AppPlan(app, @"C:\data\claude-copy", true, [], new Dictionary<string, string>(), "claude-copy", "/op.exe");
        Assert.Equal("squirrel", mode);
        Assert.EndsWith("Update.exe", plan.Target);
        Assert.Equal("--processStart", plan.Arguments[0]);
        Assert.Equal("claude.exe", plan.Arguments[1]);
        Assert.Equal("--process-start-args", plan.Arguments[2]);
        Assert.Contains("--user-data-dir=", plan.Arguments[3]);
    }

    [Fact]
    public void JetBrainsNeedsTheLauncherForItsEnvironmentVariable()
    {
        var (plan, mode) = RecipeBook.AppPlan(App(AppFamily.JetBrains, @"C:\IDEA\bin\pycharm64.exe"), "/data/x", true, [],
            new Dictionary<string, string>(), "x", "/op.exe");
        Assert.Equal("launcher", mode);
        Assert.True(plan.Environment.ContainsKey("PYCHARM_PROPERTIES"));
    }

    [Fact]
    public void WebsitesOpenAsAnAppWindowWithTheirOwnData()
    {
        var plan = RecipeBook.WebPlan(@"C:\Edge\msedge.exe", "https://web.whatsapp.com", "/data/w");
        Assert.Contains("--app=https://web.whatsapp.com", plan.Arguments);
        Assert.Contains("--user-data-dir=" + Path.Combine("/data/w", "web"), plan.Arguments);
    }
}

public class MatcherTests
{
    [Fact]
    public void InstancesWithASharedNamePrefixStayApart()
    {
        var base_ = @"C:\Users\me\AppData\Local\OpenParallel\Instances\";
        var targets = new[]
        {
            new ProcessMatcher.Target("claude-copy", @"C:\Claude\", base_ + @"claude-copy\"),
            new ProcessMatcher.Target("claude-copy-2", @"C:\Claude\", base_ + @"claude-copy-2\"),
        };
        var processes = new[]
        {
            new ProcessSnapshot(1, @"C:\Claude\claude.exe", $"\"C:\\Claude\\claude.exe\" --user-data-dir={base_}claude-copy-2\\electron"),
            new ProcessSnapshot(2, @"C:\Claude\claude.exe", $"\"C:\\Claude\\claude.exe\" --type=renderer --user-data-dir={base_}claude-copy\\electron"),
            new ProcessSnapshot(3, @"C:\Claude\claude.exe", "\"C:\\Claude\\claude.exe\""),
            new ProcessSnapshot(4, @"C:\Other\other.exe", $"--user-data-dir={base_}claude-copy\\electron"),
        };
        var (running, pending) = ProcessMatcher.Match(targets, processes);
        Assert.Equal([1], running["claude-copy-2"]);
        Assert.False(running.ContainsKey("claude-copy"));
        Assert.Equal([3], pending);
    }
}

public class ServiceTests
{
    [Fact]
    public void CreateWritesAShortcutAndManifestAndListsIt()
    {
        using var fake = new FakePlatform();
        var service = new InstanceService(fake);
        var exe = fake.MakeApp(@"Notion/Notion.exe", "resources/app.asar");

        var manifest = service.Create(new InstanceRequest { Name = "Notion Copy", AppPath = exe });

        Assert.Equal("notion-copy", manifest.InstanceSlug);
        Assert.Equal("direct", manifest.LaunchMode);
        Assert.True(manifest.ProfileIsolation);
        var shortcut = fake.Shortcuts[manifest.ShortcutPath];
        Assert.Equal(exe, shortcut.Target);
        Assert.Contains("--user-data-dir=", shortcut.Arguments);
        Assert.Equal("OpenParallel.notion-copy", shortcut.AppUserModelId);
        Assert.Single(service.List());
        Assert.True(File.Exists(Path.Combine(service.DataRootOf(manifest), "instance.json")));
    }

    [Fact]
    public void RestoreShortcutsRewritesAMissingShortcutFromTheManifest()
    {
        using var fake = new FakePlatform();
        var service = new InstanceService(fake);
        var exe = fake.MakeApp(@"Notion/Notion.exe", "resources/app.asar");
        var manifest = service.Create(new InstanceRequest { Name = "Notion Copy", AppPath = exe });
        var original = fake.Shortcuts[manifest.ShortcutPath];
        File.Delete(manifest.ShortcutPath);
        fake.Shortcuts.Remove(manifest.ShortcutPath);

        Assert.Equal([manifest.ShortcutPath], service.MissingShortcuts(manifest));
        Assert.Equal(1, service.RestoreShortcuts(manifest));

        Assert.Empty(service.MissingShortcuts(manifest));
        Assert.Equal(original, fake.Shortcuts[manifest.ShortcutPath]);
        Assert.Equal(0, service.RestoreShortcuts(manifest));
    }

    [Fact]
    public void NamesAreReservedByShortcutsAndByFoldersThatHoldData()
    {
        using var fake = new FakePlatform();
        var service = new InstanceService(fake);
        var exe = fake.MakeApp(@"Notion/Notion.exe", "resources/app.asar");
        service.Create(new InstanceRequest { Name = "Notion Copy", AppPath = exe });
        Assert.Equal("Notion Copy 2", service.ReserveName("Notion Copy"));

        // An empty skeleton is reused; a folder holding files is not.
        Directory.CreateDirectory(Path.Combine(service.InstancesRoot, "work", "cache"));
        Assert.Equal("Work", service.ReserveName("Work"));
        File.WriteAllText(Path.Combine(service.InstancesRoot, "work", "cache", "Cookies"), "x");
        Assert.Equal("Work 2", service.ReserveName("Work"));
    }

    [Fact]
    public void CopiesAreNumberedFromTwo()
    {
        Assert.Equal(["Discord 2", "Discord 3", "Discord 4"], Naming.Candidates("Discord 2").Take(3));
        Assert.Equal(["Work", "Work 2", "Work 3"], Naming.Candidates("Work").Take(3));
        Assert.Equal(["Office 365", "Office 365 2"], Naming.Candidates("Office 365").Take(2));

        using var fake = new FakePlatform();
        var service = new InstanceService(fake);
        var exe = fake.MakeApp(@"Notion/Notion.exe", "resources/app.asar");
        Assert.Equal("Notion 2", service.SuggestName("Notion"));
        service.Create(new InstanceRequest { Name = service.SuggestName("Notion"), AppPath = exe });
        Assert.Equal("Notion 3", service.SuggestName("Notion"));
        Assert.Equal("Notion 3", service.ReserveName("Notion 2"));
    }

    [Fact]
    public void AFailedCreateLeavesNothingBehind()
    {
        using var fake = new FakePlatform { FailShortcuts = true };
        var service = new InstanceService(fake);
        var exe = fake.MakeApp(@"Notion/Notion.exe", "resources/app.asar");
        Assert.Throws<IOException>(() => service.Create(new InstanceRequest { Name = "Notion Copy", AppPath = exe }));
        Assert.False(Directory.Exists(Path.Combine(service.InstancesRoot, "notion-copy")));
    }

    [Fact]
    public void RefusesAppsThatCannotBeCopied()
    {
        using var fake = new FakePlatform();
        var service = new InstanceService(fake);
        var zoom = fake.MakeApp(@"Zoom/bin/Zoom.exe");
        Assert.Throws<InvalidOperationException>(() => service.Create(new InstanceRequest { Name = "Zoom 2", AppPath = zoom }));
    }

    [Fact]
    public void RunningDetectionAndRecycleBinFollowTheRules()
    {
        using var fake = new FakePlatform();
        var service = new InstanceService(fake);
        var exe = fake.MakeApp(@"Notion/Notion.exe", "resources/app.asar");
        var manifest = service.Create(new InstanceRequest { Name = "Notion Copy", AppPath = exe });
        var dataRoot = service.DataRootOf(manifest);

        fake.RunningProcesses.Add(new ProcessSnapshot(77, exe, $"\"{exe}\" --user-data-dir={Path.Combine(dataRoot, "electron")}"));
        Assert.Equal([77], service.RunningPids(manifest));
        Assert.Throws<InvalidOperationException>(() => service.MoveToRecycleBin(manifest));
        Assert.True(service.Quit(manifest));
        Assert.Equal([77], fake.ClosedPids);

        fake.RunningProcesses.Clear();
        service.MoveToRecycleBin(manifest);
        Assert.Empty(service.List());
        Assert.False(File.Exists(manifest.ShortcutPath));
        Assert.False(Directory.Exists(dataRoot));
        Assert.Equal(2, Directory.GetFileSystemEntries(fake.RecycleBin).Length);
    }

    [Fact]
    public void EnvironmentOnlyRecipesAreRecognisedFromTheEnvironment()
    {
        using var fake = new FakePlatform();
        var service = new InstanceService(fake);
        var exe = fake.MakeApp(@"PyCharm/bin/pycharm64.exe", "idea.properties");
        var manifest = service.Create(new InstanceRequest { Name = "PyCharm Copy", AppPath = exe });
        Assert.Equal("launcher", manifest.LaunchMode);
        Assert.Equal(fake.LauncherPath, fake.Shortcuts[manifest.ShortcutPath].Target);

        fake.RunningProcesses.Add(new ProcessSnapshot(90, exe, $"\"{exe}\""));
        fake.Environments[90] = $"PYCHARM_PROPERTIES={Path.Combine(service.DataRootOf(manifest), "jetbrains", "idea.properties")}";
        Assert.Equal([90], service.RunningPids(manifest));
    }

    [Fact]
    public void WebsiteInstancesUseTheBrowserInAppMode()
    {
        using var fake = new FakePlatform();
        var service = new InstanceService(fake);
        var browser = fake.MakeApp(@"Edge/Application/msedge.exe", "120.0/chrome_elf.dll");
        var manifest = service.Create(new InstanceRequest { Name = "WhatsApp Web", AppPath = browser, WebUrl = "https://web.whatsapp.com" });
        Assert.True(manifest.IsWebsite);
        Assert.Equal("web-app", manifest.LaunchMode);
        Assert.Contains("--app=https://web.whatsapp.com", fake.Shortcuts[manifest.ShortcutPath].Arguments);
        Assert.Throws<ArgumentException>(() => service.Create(new InstanceRequest { Name = "Bad", AppPath = browser, WebUrl = "ftp://x" }));
    }
}

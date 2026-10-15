using System.Buffers.Binary;
using OpenParallel.Core;
using OpenParallel.Core.Windows;
using Xunit;

namespace OpenParallel.Core.Tests;

/// <summary>Microsoft Store (MSIX) apps: an Electron app among them gets a copy that starts
/// through Windows; other Store apps stay unsupported, with a website where one exists.</summary>
public class PackagedTests
{
    private const string ClaudeFolder = "Claude_1.0.7337.0_x64__pzs8sxrjxfjjc";

    private static string ClaudeManifest => """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
          <Identity Name="Claude" Publisher="CN=Anthropic" Version="1.0.7337.0" ProcessorArchitecture="x64" />
          <Applications>
            <Application Id="Claude" Executable="app\claude.exe" EntryPoint="Windows.FullTrustApplication">
              <uap:VisualElements DisplayName="Claude" Square44x44Logo="Assets\AppList.png" Square150x150Logo="Assets\Tile.png"
                                  Description="Claude" BackgroundColor="transparent" />
            </Application>
          </Applications>
        </Package>
        """;

    private static byte[] Png(int width)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.ToArray().CopyTo(bytes, 12);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), width);
        return bytes;
    }

    /// <summary>A fake Claude package: the exe, Electron's app.asar, the manifest and logos.</summary>
    private static string MakeClaude(FakePlatform fake)
    {
        var exe = fake.MakeApp($"WindowsApps/{ClaudeFolder}/app/claude.exe", "resources/app.asar");
        var root = PackagedApps.PackageRoot(exe)!;
        File.WriteAllText(Path.Combine(root, "AppxManifest.xml"), ClaudeManifest);
        var assets = Directory.CreateDirectory(Path.Combine(root, "Assets")).FullName;
        File.WriteAllBytes(Path.Combine(assets, "AppList.scale-200.png"), Png(88));
        File.WriteAllBytes(Path.Combine(assets, "AppList.targetsize-256.png"), Png(256));
        File.WriteAllBytes(Path.Combine(assets, "AppList.targetsize-256_altform-unplated.png"), Png(256));
        File.WriteAllBytes(Path.Combine(assets, "AppList.targetsize-256_altform-lightunplated.png"), Png(256));
        return exe;
    }

    [Theory]
    [InlineData("Claude_1.0.7337.0_x64__pzs8sxrjxfjjc", "Claude_pzs8sxrjxfjjc")]
    [InlineData("5319275A.WhatsAppDesktop_2.2540.6.0_x64__cv1g1gvanyjgm", "5319275A.WhatsAppDesktop_cv1g1gvanyjgm")]
    [InlineData("NoUnderscores", null)]
    public void FamilyNameDropsVersionAndArchitecture(string folder, string? family) =>
        Assert.Equal(family, PackagedApps.FamilyName(folder));

    [Fact]
    public void AnElectronStoreAppIsExperimentalAndStartsThroughWindows()
    {
        using var fake = new FakePlatform();
        var exe = MakeClaude(fake);

        var app = AppInspector.Inspect(exe);
        Assert.Equal(AppFamily.Electron, app.Family);
        Assert.Equal(InstallKind.Packaged, app.InstallKind);
        Assert.Equal(CompatibilityStatus.Experimental, app.Status);
        Assert.Equal("Claude_pzs8sxrjxfjjc!Claude", app.PackageId);

        var (plan, mode) = RecipeBook.AppPlan(app, "/data/claude-parall", true, [], new Dictionary<string, string>(), "claude-parall", "/op.exe");
        Assert.Equal("packaged", mode);
        Assert.Equal(@"shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude", plan.Target);
        Assert.Equal(["--user-data-dir=" + Path.Combine("/data/claude-parall", "electron")], plan.Arguments);
        // Updates move the app to a new package folder; recognition keys on WindowsApps itself.
        Assert.Equal(Path.Combine(fake.Root, "Apps", "WindowsApps"), RecipeBook.ProcessFolder(app));
    }

    [Fact]
    public void OtherStoreAppsStayUnsupportedWithAWebsiteWhereOneExists()
    {
        using var fake = new FakePlatform();
        var exe = fake.MakeApp("WindowsApps/5319275A.WhatsAppDesktop_2.2540.6.0_x64__cv1g1gvanyjgm/WhatsApp.exe");
        File.WriteAllText(Path.Combine(PackagedApps.PackageRoot(exe)!, "AppxManifest.xml"), ClaudeManifest
            .Replace("Name=\"Claude\"", "Name=\"5319275A.WhatsAppDesktop\"").Replace(@"app\claude.exe", "WhatsApp.exe"));

        var app = AppInspector.Inspect(exe);
        Assert.Equal(CompatibilityStatus.NotSupported, app.Status);
        Assert.False(app.CanCreate);
        Assert.Equal("https://web.whatsapp.com", app.WebAlternative);
    }

    [Fact]
    public void TheShortcutIconIsTheLargestTransparentLogo()
    {
        using var fake = new FakePlatform();
        var exe = MakeClaude(fake);
        var package = PackagedApps.Find(exe)!;

        Assert.EndsWith("AppList.targetsize-256_altform-unplated.png", PackagedApps.LargestLogo(package));
        var ico = Path.Combine(fake.Root, "icon.ico");
        Assert.True(PackagedApps.WriteIcon(exe, ico));
        var bytes = File.ReadAllBytes(ico);
        Assert.Equal([0, 0, 1, 0, 1, 0], bytes[..6]);
        Assert.Equal(0, bytes[6]); // 256 px is written as 0
        Assert.Equal(22, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18)));
        Assert.Equal(0x89, bytes[22]);
    }

    [Fact]
    public void ACopyOpensThroughTheLauncherAndIsRecognisedAfterAnUpdate()
    {
        using var fake = new FakePlatform();
        var service = new InstanceService(fake);
        var manifest = service.Create(new InstanceRequest { Name = "Claude Parall", AppPath = MakeClaude(fake) });

        Assert.Equal("packaged", manifest.LaunchMode);
        var dataRoot = service.DataRootOf(manifest);
        var profile = Path.Combine(dataRoot, "electron");
        Assert.True(Directory.Exists(profile), "the profile folder exists before the first start");
        var shortcut = fake.Shortcuts[manifest.ShortcutPath];
        Assert.Equal(fake.LauncherPath, shortcut.Target);
        Assert.Equal(["--launch", "claude-parall"], shortcut.ArgumentList);
        Assert.Equal(Path.Combine(dataRoot, InstanceService.IconFileName), shortcut.IconPath);

        service.Launch(manifest);
        Assert.Equal(@"shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude", fake.Started[^1].Target);
        Assert.Contains($"--user-data-dir={profile}", fake.Started[^1].Arguments);

        var updated = Path.Combine(fake.Root, "Apps", "WindowsApps", "Claude_1.0.8000.0_x64__pzs8sxrjxfjjc", "app", "claude.exe");
        fake.RunningProcesses.Add(new ProcessSnapshot(4242, updated, $"\"{updated}\" --user-data-dir={profile}"));
        fake.RunningProcesses.Add(new ProcessSnapshot(4243, updated, $"\"{updated}\""));
        Assert.Equal([4242], service.RunningPids(manifest));
    }
}

/// <summary>A VS Code-family copy runs the program the original installed, so it leaves
/// updating to the original (two updaters at once fail on Windows).</summary>
public class UpdaterTests
{
    private static string Settings(string dataRoot) => Path.Combine(RecipeBook.VSCodeUserData(dataRoot), "User", "settings.json");

    [Fact]
    public void ANewEditorCopyStartsWithItsUpdaterOff()
    {
        using var fake = new FakePlatform();
        var service = new InstanceService(fake);
        var exe = fake.MakeApp("cursor/Cursor.exe", "resources/app/product.json");

        var manifest = service.Create(new InstanceRequest { Name = "Cursor Parall", AppPath = exe });

        var text = File.ReadAllText(Settings(service.DataRootOf(manifest)));
        Assert.Contains("\"update.mode\": \"none\"", text);
        Assert.Contains("\"update.enableWindowsBackgroundUpdates\": false", text);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, System.Text.Json.JsonDocument.Parse(text).RootElement.ValueKind);
    }

    [Fact]
    public void AnOlderCopyGetsItWhenOpenedKeepingItsOwnSettings()
    {
        using var fake = new FakePlatform();
        var service = new InstanceService(fake);
        var manifest = service.Create(new InstanceRequest { Name = "Cursor Parall", AppPath = fake.MakeApp("cursor/Cursor.exe", "resources/app/product.json") });
        var settings = Settings(service.DataRootOf(manifest));
        File.WriteAllText(settings, "{\n  \"editor.fontSize\": 15\n}\n");

        service.Launch(manifest);

        var parsed = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settings)).RootElement;
        Assert.Equal("none", parsed.GetProperty("update.mode").GetString());
        Assert.Equal(15, parsed.GetProperty("editor.fontSize").GetInt32());
    }

    [Fact]
    public void AnUpdateModeThePersonChoseIsLeftAlone()
    {
        using var fake = new FakePlatform();
        var root = Path.Combine(fake.Root, "copy");
        Directory.CreateDirectory(Path.GetDirectoryName(Settings(root))!);
        File.WriteAllText(Settings(root), "{ \"update.mode\": \"manual\" }");
        RecipeBook.LeaveUpdatesToTheOriginal(root);
        Assert.Equal("{ \"update.mode\": \"manual\" }", File.ReadAllText(Settings(root)));

        File.WriteAllText(Settings(root), "{}");
        RecipeBook.LeaveUpdatesToTheOriginal(root);
        Assert.Equal("none", System.Text.Json.JsonDocument.Parse(File.ReadAllText(Settings(root))).RootElement.GetProperty("update.mode").GetString());
    }
}

/// <summary>Entries found in the Windows VM test (2026-10-15) that are not apps to copy.</summary>
public class AppListNoiseTests
{
    private const string Windows = @"C:\Windows";

    [Theory]
    [InlineData(@"C:\Program Files\Mozilla Firefox\private_browsing.exe")]
    [InlineData(@"C:\ProgramData\Package Cache\{6c1e1f5a-0000-4e2b-9d6a-1b2c3d4e5f60}\dotnet-sdk-6.0.428-win-arm64.exe")]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    [InlineData(@"C:\Users\ali\AppData\Local\Programs\cursor\unins000.exe")]
    public void HelpersAndInstallerCachesAreSkipped(string exe) =>
        Assert.True(AppListNoise.IsNoisePath(exe, Windows));

    [Theory]
    [InlineData(@"C:\Program Files\Mozilla Firefox\firefox.exe")]
    [InlineData(@"C:\Users\ali\AppData\Local\Programs\cursor\Cursor.exe")]
    public void RealAppsStay(string exe) =>
        Assert.False(AppListNoise.IsNoisePath(exe, Windows));
}

/// <summary>Store apps reach the app list through their manifests (they have no shortcut file).</summary>
public class StoreListingTests
{
    private const string Root = @"C:\Program Files\WindowsApps\Claude_2.42304.0.0_arm64__pzs8sxrjxfjjc";

    /// <summary>Shaped like Claude 2.42304's real manifest: the app and two hidden helpers.</summary>
    private const string Claude = """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
          <Identity Name="Claude" Publisher="CN=Anthropic" Version="2.42304.0.0" ProcessorArchitecture="arm64" />
          <Properties><DisplayName>Claude</DisplayName></Properties>
          <Applications>
            <Application Id="Claude" Executable="app\Claude.exe" EntryPoint="Windows.FullTrustApplication">
              <uap:VisualElements DisplayName="Claude" Square44x44Logo="Assets\Square44x44Logo.png" Description="Claude" BackgroundColor="transparent" />
            </Application>
            <Application Id="SshAskpass" Executable="app\resources\claude-ssh-askpass.exe" EntryPoint="Windows.FullTrustApplication">
              <uap:VisualElements DisplayName="Claude" AppListEntry="none" Square44x44Logo="Assets\Square44x44Logo.png" Description="helper" BackgroundColor="transparent" />
            </Application>
            <Application Id="SshProxy" Executable="app\resources\claude-ssh-proxy.exe" EntryPoint="Windows.FullTrustApplication">
              <uap:VisualElements DisplayName="Claude" AppListEntry="none" Square44x44Logo="Assets\Square44x44Logo.png" Description="helper" BackgroundColor="transparent" />
            </Application>
          </Applications>
        </Package>
        """;

    [Fact]
    public void OnlyTheAppItselfIsListed()
    {
        var app = Assert.Single(PackagedApps.ListedApps(Root, Claude));
        Assert.Equal("Claude", app.Name);
        Assert.Equal("Claude_pzs8sxrjxfjjc", app.Family);
        Assert.EndsWith("Claude.exe", app.Exe);
        Assert.Equal(new Version(2, 42304, 0, 0), app.Version);
    }

    [Fact]
    public void UwpAppsFrameworksAndWindowsOwnPackagesAreLeftOut()
    {
        var uwp = Claude.Replace("Executable=\"app\\Claude.exe\" EntryPoint=\"Windows.FullTrustApplication\"", "Executable=\"App.exe\" EntryPoint=\"App.App\"");
        Assert.Empty(PackagedApps.ListedApps(Root, uwp));

        var framework = Claude.Replace("<DisplayName>Claude</DisplayName>", "<DisplayName>Claude</DisplayName><Framework>true</Framework>");
        Assert.Empty(PackagedApps.ListedApps(Root, framework));

        var notepad = Claude.Replace("Name=\"Claude\"", "Name=\"Microsoft.WindowsNotepad\"");
        Assert.Empty(PackagedApps.ListedApps(@"C:\Program Files\WindowsApps\Microsoft.WindowsNotepad_11.2607.14.0_arm64__8wekyb3d8bbwe", notepad));
        Assert.Empty(PackagedApps.ListedApps(Root, "not xml"));
    }

    [Fact]
    public void AResourceNameFallsBackToThePackageName()
    {
        var unnamed = Claude.Replace("<uap:VisualElements DisplayName=\"Claude\" Square44x44Logo", "<uap:VisualElements DisplayName=\"ms-resource:AppName\" Square44x44Logo")
            .Replace("<DisplayName>Claude</DisplayName>", "<DisplayName>ms-resource:Title</DisplayName>");
        Assert.Equal("Claude", Assert.Single(PackagedApps.ListedApps(Root, unnamed)).Name);
    }
}

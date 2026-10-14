namespace OpenParallel.Core.Windows;

/// <summary>Windows apps: exe inspection (<see cref="AppInspector"/>), recipes
/// (<see cref="RecipeBook"/>), Start menu discovery and browsers.</summary>
public sealed class WindowsAppKnowledge : IAppKnowledge
{
    public InspectedApp Inspect(string path) => AppInspector.Inspect(path, InstanceService.Ledger);

    public (LaunchPlan Plan, string Mode) AppPlan(InspectedApp app, string dataRoot, bool separateData,
        IReadOnlyList<string> extraArguments, IReadOnlyDictionary<string, string> extraEnvironment, string slug, string launcherPath) =>
        RecipeBook.AppPlan(app, dataRoot, separateData, extraArguments, extraEnvironment, slug, launcherPath);

    public LaunchPlan WebPlan(InspectedApp browser, string url, string dataRoot, string slug) =>
        RecipeBook.WebPlan(browser.ExePath, url, dataRoot);

    public string ProcessFolder(InspectedApp app) => RecipeBook.ProcessFolder(app);

    public string? ExtraDataPath(InspectedApp app, string slug) => null;

    public void Prepare(InspectedApp app, string dataRoot, bool separateData)
    {
        if (app.InstallKind == InstallKind.Packaged)
        {
            // Windows moves a Store app's new folders under AppData into the package's own
            // storage, but uses a folder that already exists as is: make it first, so the
            // copy's data stays in its instance folder.
            if (separateData && app.Family == AppFamily.Electron) Directory.CreateDirectory(RecipeBook.ElectronProfile(dataRoot));
            PackagedApps.WriteIcon(app.ExePath, Path.Combine(dataRoot, InstanceService.IconFileName));
        }
        if (separateData && app.Family == AppFamily.VSCode) RecipeBook.LeaveUpdatesToTheOriginal(dataRoot);
        if (!separateData || app.Family != AppFamily.JetBrains) return;
        Directory.CreateDirectory(Path.Combine(dataRoot, "jetbrains"));
        File.WriteAllText(Path.Combine(dataRoot, "jetbrains", "idea.properties"), RecipeBook.JetBrainsProperties(dataRoot));
    }

    public IReadOnlyList<InstalledApp> InstalledApps(IPlatform platform) =>
        OperatingSystem.IsWindows() ? Windows.InstalledApps.Find(platform) : [];

    public IReadOnlyList<string> Browsers(IEnumerable<InspectedApp>? known = null)
    {
        var found = Windows.Browsers.Find().ToList();
        foreach (var app in known ?? [])
        {
            if (app.Family == AppFamily.Chromium && !found.Contains(app.ExePath, StringComparer.OrdinalIgnoreCase)) found.Add(app.ExePath);
        }
        return found;
    }

    public string? ResolvePicked(IPlatform platform, string path)
    {
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var shortcut = platform.ReadShortcut(path);
            return shortcut is null || !OperatingSystem.IsWindows() ? null : Windows.InstalledApps.ResolveExe(shortcut.Target, shortcut.Arguments);
        }
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }
}

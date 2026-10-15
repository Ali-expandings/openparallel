using System.Runtime.Versioning;
using Microsoft.Win32;

namespace OpenParallel.Core.Windows;

[SupportedOSPlatform("windows")]
public static class InstalledApps
{
    /// <summary>Start menu shortcuts (all users and this user) resolved to their exe, the
    /// Uninstall registry and Microsoft Store apps. Skips uninstallers, Windows' own tools and
    /// OpenParallel's shortcuts.</summary>
    public static IReadOnlyList<InstalledApp> Find(IPlatform platform)
    {
        var found = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        var ownFolder = platform.ShortcutsFolder;
        var startMenuPrograms = Path.GetDirectoryName(ownFolder)!;
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            startMenuPrograms,
        };
        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> links;
            try
            {
                links = Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
            }
            catch (IOException)
            {
                continue;
            }
            foreach (var link in links)
            {
                if (link.StartsWith(ownFolder, StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileNameWithoutExtension(link);
                if (IsNoise(name)) continue;
                var shortcut = platform.ReadShortcut(link);
                if (shortcut is null) continue;
                var exe = ResolveExe(shortcut.Target, shortcut.Arguments);
                if (exe is null || IsNoisePath(exe)) continue;
                found.TryAdd(exe, new InstalledApp(name, exe));
            }
        }

        foreach (var (name, exe) in UninstallEntries())
        {
            if (IsNoise(name) || IsNoisePath(exe) || found.ContainsKey(exe)) continue;
            found[exe] = new InstalledApp(name, exe);
        }

        foreach (var app in StoreApps())
        {
            if (IsNoisePath(app.Exe) || found.ContainsKey(app.Exe)) continue;
            found[app.Exe] = new InstalledApp(app.Name, app.Exe);
        }

        return found.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>A shortcut's real app: Squirrel shortcuts point at <c>Update.exe --processStart X.exe</c>.</summary>
    public static string? ResolveExe(string target, string arguments)
    {
        if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target)) return null;
        if (Path.GetFileName(target).Equals("Update.exe", StringComparison.OrdinalIgnoreCase))
        {
            var args = WindowsCommandLine.Split(arguments);
            var index = args.ToList().FindIndex(a => a.Equals("--processStart", StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index + 1 < args.Count)
            {
                var root = Path.GetDirectoryName(target)!;
                var newest = AppInspector.NewestAppFolder(root);
                var exe = newest is null ? null : Path.Combine(newest, args[index + 1]);
                return exe is not null && File.Exists(exe) ? exe : null;
            }
        }
        return target;
    }

    private const string PackagesKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    /// <summary>Microsoft Store apps have neither a shortcut file nor an Uninstall entry. Windows
    /// keeps this user's packages, with their folders, under <see cref="PackagesKey"/>; an
    /// updated app can be listed twice for a while, so only its newest version is kept.</summary>
    private static List<PackagedApps.ListedApp> StoreApps()
    {
        var apps = new List<PackagedApps.ListedApp>();
        try
        {
            using var packages = Registry.CurrentUser.OpenSubKey(PackagesKey);
            if (packages is null) return apps;
            foreach (var fullName in packages.GetSubKeyNames())
            {
                if (PackagedApps.IsWindowsOwn(fullName.Split('_')[0])) continue;
                using var key = packages.OpenSubKey(fullName);
                if (key?.GetValue("PackageRootFolder") is not string root) continue;
                try
                {
                    var manifest = Path.Combine(root, "AppxManifest.xml");
                    if (File.Exists(manifest)) apps.AddRange(PackagedApps.ListedApps(root, File.ReadAllText(manifest)));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // A package being installed or removed; it shows up on the next look.
                }
            }
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return apps;
        }
        return apps.GroupBy(a => (a.Family, a.AppId)).Select(g => g.MaxBy(a => a.Version)!).ToList();
    }

    private static IEnumerable<(string Name, string Exe)> UninstallEntries()
    {
        var keys = new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
        };
        foreach (var (hive, view) in keys)
        {
            RegistryKey? uninstall;
            try
            {
                uninstall = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            }
            catch (Exception)
            {
                continue;
            }
            if (uninstall is null) continue;
            using (uninstall)
            {
                foreach (var subName in uninstall.GetSubKeyNames())
                {
                    using var sub = uninstall.OpenSubKey(subName);
                    if (sub is null || sub.GetValue("SystemComponent") is int and 1) continue;
                    var name = sub.GetValue("DisplayName") as string;
                    var icon = sub.GetValue("DisplayIcon") as string;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(icon)) continue;
                    var exe = icon.Split(',')[0].Trim().Trim('"');
                    if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(exe)) yield return (name.Trim(), exe);
                }
            }
        }
    }

    private static bool IsNoise(string name) =>
        name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
        || name.Contains("readme", StringComparison.OrdinalIgnoreCase)
        || name.Contains("help", StringComparison.OrdinalIgnoreCase)
        || name.Contains("website", StringComparison.OrdinalIgnoreCase);

    private static bool IsNoisePath(string exe) =>
        AppListNoise.IsNoisePath(exe, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
}

/// <summary>Entries the app list skips. Kept apart from <see cref="InstalledApps"/> so tests run on any system.</summary>
internal static class AppListNoise
{
    /// <summary>Helpers that only start another app (Firefox's private window launcher) and
    /// OpenParallel itself.</summary>
    private static readonly string[] Files = ["OpenParallel.exe", "opctl.exe", "private_browsing.exe"];

    /// <summary>Windows' own tools, uninstallers, those helpers, and installer caches (the .NET SDK's
    /// uninstall entry points at its setup exe under Package Cache).</summary>
    public static bool IsNoisePath(string exe, string windowsFolder)
    {
        var file = exe[(exe.LastIndexOfAny(['\\', '/']) + 1)..];
        return (windowsFolder.Length > 0 && exe.StartsWith(windowsFolder, StringComparison.OrdinalIgnoreCase))
            || file.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
            || Files.Contains(file, StringComparer.OrdinalIgnoreCase)
            || exe.Contains(@"\Package Cache\", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Chromium browsers that can open a website instance, best first.</summary>
public static class Browsers
{
    public static IReadOnlyList<string> Candidates()
    {
        string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "";
        var programFiles = Env("ProgramFiles");
        var programFilesX86 = Env("ProgramFiles(x86)");
        var local = Env("LOCALAPPDATA");
        return
        [
            Path.Combine(programFilesX86, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(programFiles, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(programFiles, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(programFilesX86, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(programFiles, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
            Path.Combine(local, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
            Path.Combine(local, @"Vivaldi\Application\vivaldi.exe"),
            Path.Combine(programFiles, @"Chromium\Application\chrome.exe"),
        ];
    }

    /// <summary>Installed browsers: the known locations, then any Chromium app in the list.</summary>
    public static IReadOnlyList<string> Find(IEnumerable<string>? extra = null)
    {
        var found = Candidates().Where(p => p.Length > 0 && File.Exists(p)).ToList();
        if (extra is not null)
        {
            foreach (var exe in extra)
            {
                if (found.Contains(exe, StringComparer.OrdinalIgnoreCase)) continue;
                if (AppInspector.Inspect(exe).Family == AppFamily.Chromium) found.Add(exe);
            }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

using System.Diagnostics;

namespace OpenParallel.Core;

/// <summary>Works out what kind of app an exe is from files that ship beside it. Reads only;
/// never touches the app.</summary>
public static class AppInspector
{
    private static readonly HashSet<string> ChromiumExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe", "msedge.exe", "brave.exe", "vivaldi.exe", "chromium.exe", "thorium.exe", "opera.exe",
        "launcher.exe" /* Opera */,
    };

    private static readonly HashSet<string> FirefoxExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "firefox.exe", "librewolf.exe", "waterfox.exe", "floorp.exe", "zen.exe", "mercury.exe",
    };

    /// <summary>Apps that enforce a single running copy no matter what, with a website that
    /// works as an alternative where one exists.</summary>
    private static readonly Dictionary<string, (string Name, string? Web)> SingleInstance = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zoom.exe"] = ("Zoom", "https://app.zoom.us"),
        ["steam.exe"] = ("Steam", null),
        ["whatsapp.exe"] = ("WhatsApp", "https://web.whatsapp.com"),
        ["whatsapp.root.exe"] = ("WhatsApp", "https://web.whatsapp.com"),
        ["ms-teams.exe"] = ("Microsoft Teams", "https://teams.microsoft.com"),
        ["outlook.exe"] = ("Outlook", "https://outlook.live.com"),
        ["olk.exe"] = ("Outlook", "https://outlook.live.com"),
        ["winword.exe"] = ("Word", null),
        ["excel.exe"] = ("Excel", null),
        ["powerpnt.exe"] = ("PowerPoint", null),
        ["onenote.exe"] = ("OneNote", null),
        ["spotify.exe"] = ("Spotify", "https://open.spotify.com"),
    };

    public static InspectedApp Inspect(string exePath, IReadOnlyDictionary<(string, string), CompatibilityStatus>? ledger = null)
    {
        exePath = Path.GetFullPath(exePath);
        var exeName = Path.GetFileName(exePath);
        var dir = Path.GetDirectoryName(exePath) ?? "";
        var (product, version, company) = VersionInfo(exePath);
        var name = !string.IsNullOrWhiteSpace(product) ? product.Trim() : Path.GetFileNameWithoutExtension(exePath);

        InspectedApp Make(AppFamily family, CompatibilityStatus status, string notes, InstallKind kind = InstallKind.Plain,
            string? update = null, string? root = null, string? web = null)
        {
            if (ledger is not null && status is CompatibilityStatus.Supported or CompatibilityStatus.Experimental
                && ledger.TryGetValue((exeName.ToLowerInvariant(), version), out var recorded))
            {
                status = recorded;
            }
            return new InspectedApp
            {
                ExePath = exePath, Name = name, Version = version, Company = company, Family = family,
                Status = status, Notes = notes, InstallKind = kind, UpdateExePath = update, InstallRoot = root,
                WebAlternative = web,
            };
        }

        if (exePath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) || Windows.PackagedApps.PackageRoot(exePath) is not null)
        {
            // Store apps start through Windows. An Electron app among them still takes
            // --user-data-dir, so its copy can get its own data; other Store apps can't.
            var package = Windows.PackagedApps.Find(exePath);
            if (package is not null && (File.Exists(Path.Combine(dir, "resources", "app.asar")) || File.Exists(Path.Combine(dir, "resources", "app", "package.json"))))
            {
                // OpenAI's ChatGPT app runs Codex, which keeps its sign-in and sessions in
                // %USERPROFILE%\.codex unless CODEX_HOME says otherwise, and Windows starts a Store
                // app without passing environment variables: Codex stays shared.
                var codex = package.Name.Equals("OpenAI.Codex", StringComparison.OrdinalIgnoreCase)
                    || File.Exists(Path.Combine(dir, "resources", "codex.exe"));
                return Make(AppFamily.Electron, codex ? CompatibilityStatus.SharesData : CompatibilityStatus.Experimental,
                    "A Microsoft Store app built on Electron. Its copy starts through Windows with a data folder of its own. " +
                    (codex
                        ? "Codex inside it still uses your .codex folder, so Codex's sign-in and sessions are shared with the original."
                        : "New on Windows: test before relying on it."),
                    InstallKind.Packaged) with { PackageId = package.AppUserModelId };
            }
            var web = package is null ? null : Windows.PackagedApps.WebAlternative(package.Name);
            return Make(AppFamily.Packaged, CompatibilityStatus.NotSupported,
                "Apps from the Microsoft Store run in a container Windows manages, so a second copy cannot get its own data." +
                (web is null ? "" : " Create a website instance instead; each keeps its own login."),
                InstallKind.Packaged, web: web);
        }

        if (SingleInstance.TryGetValue(exeName, out var single))
        {
            return Make(AppFamily.Unsupported, CompatibilityStatus.NotSupported,
                $"{single.Name} allows only one running copy on a computer." +
                (single.Web is null ? "" : " Create a website instance instead; each keeps its own login."),
                web: single.Web);
        }

        // Squirrel installs keep the real exe in app-x.y.z and an Update.exe one level up.
        var (squirrelRoot, squirrelUpdate) = FindSquirrel(dir, exeName);
        var kind = squirrelUpdate is null ? InstallKind.Plain : InstallKind.Squirrel;
        // For a Squirrel stub in the root folder, inspect the newest real app folder.
        var appDir = dir;
        if (squirrelRoot is not null && string.Equals(dir.TrimEnd('\\', '/'), squirrelRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            appDir = NewestAppFolder(squirrelRoot) ?? dir;

        if (exeName.Equals("telegram.exe", StringComparison.OrdinalIgnoreCase))
            return Make(AppFamily.Telegram, CompatibilityStatus.Supported,
                "Telegram Desktop runs extra copies with -many, each in its own folder.", kind, squirrelUpdate, squirrelRoot);

        if (FirefoxExes.Contains(exeName) || (File.Exists(Path.Combine(appDir, "application.ini")) && File.Exists(Path.Combine(appDir, "omni.ja"))))
            return Make(AppFamily.Firefox, CompatibilityStatus.Supported,
                "Firefox-family browsers keep a separate profile per copy.", kind, squirrelUpdate, squirrelRoot);

        if (ChromiumExes.Contains(exeName) || HasChromeElf(appDir))
            return Make(AppFamily.Chromium, CompatibilityStatus.Supported,
                "Chromium browsers keep separate sign-ins, cookies and extensions per user-data folder.", kind, squirrelUpdate, squirrelRoot);

        var resources = Path.Combine(appDir, "resources");
        if (File.Exists(Path.Combine(resources, "app", "product.json")))
            return Make(AppFamily.VSCode, CompatibilityStatus.Supported,
                "VS Code-style editors keep separate settings, sign-ins and extensions per copy. Updates come from the original app.", kind, squirrelUpdate, squirrelRoot);

        if (File.Exists(Path.Combine(resources, "app.asar")) || File.Exists(Path.Combine(resources, "app", "package.json")))
            return Make(AppFamily.Electron, CompatibilityStatus.Supported,
                "This app is built on Electron, which keeps sign-ins and data in a folder OpenParallel can give each copy.",
                kind, squirrelUpdate, squirrelRoot);

        if (File.Exists(Path.Combine(appDir, "idea.properties")) || File.Exists(Path.Combine(Path.GetDirectoryName(appDir) ?? appDir, "bin", "idea.properties")))
            return Make(AppFamily.JetBrains, CompatibilityStatus.Experimental,
                "JetBrains IDEs can use separate settings and caches per copy. Test before relying on it.", kind, squirrelUpdate, squirrelRoot);

        return Make(AppFamily.Generic, CompatibilityStatus.SharesData,
            "OpenParallel can open another copy, but it has no way to give this app its own data, so every copy uses the same account.",
            kind, squirrelUpdate, squirrelRoot);
    }

    private static (string Product, string Version, string Company) VersionInfo(string exePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var version = !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion! : info.FileVersion ?? "";
            return (info.ProductName ?? "", version.Trim(), info.CompanyName ?? "");
        }
        catch
        {
            return ("", "", "");
        }
    }

    private static bool HasChromeElf(string dir)
    {
        if (File.Exists(Path.Combine(dir, "chrome_elf.dll"))) return true;
        // Chrome and Edge keep it in a version-numbered subfolder.
        try
        {
            return Directory.EnumerateDirectories(dir).Any(d => File.Exists(Path.Combine(d, "chrome_elf.dll")));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns the Squirrel install root and its Update.exe, if the exe belongs to one.</summary>
    internal static (string? Root, string? UpdateExe) FindSquirrel(string exeDir, string exeName)
    {
        foreach (var candidate in new[] { exeDir, Path.GetDirectoryName(exeDir) })
        {
            if (candidate is null) continue;
            var update = Path.Combine(candidate, "Update.exe");
            if (File.Exists(update) && Directory.EnumerateDirectories(candidate, "app-*").Any(d => File.Exists(Path.Combine(d, exeName))))
                return (candidate, update);
        }
        return (null, null);
    }

    internal static string? NewestAppFolder(string squirrelRoot)
    {
        return Directory.EnumerateDirectories(squirrelRoot, "app-*")
            .Select(d => (Dir: d, Version: Version.TryParse(Path.GetFileName(d)[4..], out var v) ? v : new Version(0, 0)))
            .OrderByDescending(x => x.Version)
            .Select(x => x.Dir)
            .FirstOrDefault();
    }
}

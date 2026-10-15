namespace OpenParallel.Core;

/// <summary>Turns an app and an instance's data folder into the exact way to start it.</summary>
public static class RecipeBook
{
    /// <summary>An Electron copy's user-data folder.</summary>
    public static string ElectronProfile(string dataRoot) => Path.Combine(dataRoot, "electron");

    /// <summary>A VS Code-family copy's user-data folder.</summary>
    public static string VSCodeUserData(string dataRoot) => Path.Combine(dataRoot, "vscode", "user-data");

    /// <summary>Turns off a VS Code-family copy's own updater. The copy runs the program the
    /// original installed, and two updaters at once fail (Cursor and VS Code on Windows: Inno
    /// Setup's "Setup is currently running"); the original's updates reach the copy anyway.
    /// Leaves the file alone if the person already chose an update mode for this copy.</summary>
    public static void LeaveUpdatesToTheOriginal(string dataRoot)
    {
        const string keys = "\"update.mode\": \"none\",\n  \"update.enableWindowsBackgroundUpdates\": false";
        var settings = Path.Combine(VSCodeUserData(dataRoot), "User", "settings.json");
        if (!File.Exists(settings))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            File.WriteAllText(settings, "{\n  " + keys + "\n}\n");
            return;
        }
        var text = File.ReadAllText(settings);
        var brace = text.IndexOf('{');
        if (brace < 0 || text.Contains("\"update.mode\"", StringComparison.Ordinal)) return;
        var rest = text[(brace + 1)..];
        var empty = rest.TrimStart().StartsWith('}');
        File.WriteAllText(settings, text[..(brace + 1)] + "\n  " + keys + (empty ? "\n" : ",") + rest);
    }

    /// <summary>Arguments that point the app at the instance's own data. Empty when the app
    /// has no such switch.</summary>
    public static IReadOnlyList<string> ProfileArguments(AppFamily family, string dataRoot) => family switch
    {
        AppFamily.Chromium => [$"--user-data-dir={Path.Combine(dataRoot, "chromium")}"],
        AppFamily.Electron => [$"--user-data-dir={ElectronProfile(dataRoot)}"],
        AppFamily.VSCode =>
        [
            "--user-data-dir", VSCodeUserData(dataRoot),
            "--extensions-dir", Path.Combine(dataRoot, "vscode", "extensions"),
        ],
        AppFamily.Firefox => ["-profile", Path.Combine(dataRoot, "firefox"), "-no-remote"],
        AppFamily.Telegram => ["-many", "-workdir", Path.Combine(dataRoot, "telegram")],
        _ => [],
    };

    /// <summary>The folders <see cref="ProfileArguments"/> points the app at. They are made before the
    /// first start: Firefox from a Snap refuses a <c>-profile</c> folder that doesn't exist yet
    /// ("Your Firefox profile cannot be loaded").</summary>
    public static IReadOnlyList<string> ProfileFolders(AppFamily family, string dataRoot) => family switch
    {
        AppFamily.Chromium => [Path.Combine(dataRoot, "chromium")],
        AppFamily.Electron => [ElectronProfile(dataRoot)],
        AppFamily.VSCode => [VSCodeUserData(dataRoot), Path.Combine(dataRoot, "vscode", "extensions")],
        AppFamily.Firefox => [Path.Combine(dataRoot, "firefox")],
        AppFamily.Telegram => [Path.Combine(dataRoot, "telegram")],
        AppFamily.Website => [Path.Combine(dataRoot, "web")],
        _ => [],
    };

    /// <summary>Environment variables a recipe needs (empty for most).</summary>
    public static IReadOnlyDictionary<string, string> ProfileEnvironment(InspectedApp app, string dataRoot)
    {
        if (app.Family != AppFamily.JetBrains) return new Dictionary<string, string>();
        var product = JetBrainsProduct(Path.GetFileName(app.ExePath));
        return new Dictionary<string, string> { [$"{product}_PROPERTIES"] = Path.Combine(dataRoot, "jetbrains", "idea.properties") };
    }

    /// <summary>The <c>idea.properties</c> a JetBrains instance reads.</summary>
    public static string JetBrainsProperties(string dataRoot)
    {
        string P(string sub) => Path.Combine(dataRoot, "jetbrains", sub).Replace('\\', '/');
        return $"idea.config.path={P("config")}\nidea.system.path={P("system")}\nidea.plugins.path={P("plugins")}\nidea.log.path={P("logs")}\n";
    }

    public static string JetBrainsProduct(string exeName)
    {
        var file = exeName.Split('\\', '/')[^1];
        var stem = Path.GetFileNameWithoutExtension(file).ToLowerInvariant().TrimEnd('6', '4');
        return stem switch
        {
            "idea" => "IDEA",
            "pycharm" => "PYCHARM",
            "webstorm" => "WEBIDE",
            "phpstorm" => "PHPSTORM",
            "clion" => "CLION",
            "rider" => "RIDER",
            "goland" => "GOLAND",
            "rubymine" => "RUBYMINE",
            "datagrip" => "DATAGRIP",
            "rustrover" => "RUSTROVER",
            _ => "IDEA",
        };
    }

    /// <summary>Folder every process of this instance runs from (used to recognise it).</summary>
    public static string ProcessFolder(InspectedApp app) => app.InstallKind switch
    {
        InstallKind.Squirrel when app.InstallRoot is not null => app.InstallRoot,
        // A Store app moves to a new package folder on every update: match on WindowsApps
        // itself (the data folder on the command line keeps the match exact).
        InstallKind.Packaged when Windows.PackagedApps.PackageRoot(app.ExePath) is { } root => Path.GetDirectoryName(root)!,
        _ => Path.GetDirectoryName(app.ExePath)!,
    };

    /// <summary>The launch plan for an app instance. <paramref name="launcherExe"/> is
    /// OpenParallel.exe, used only when environment variables are needed.</summary>
    public static (LaunchPlan Plan, string Mode) AppPlan(InspectedApp app, string dataRoot, bool separateData,
        IReadOnlyList<string> extraArguments, IReadOnlyDictionary<string, string> extraEnvironment, string slug, string launcherExe)
    {
        var arguments = new List<string>();
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (separateData)
        {
            arguments.AddRange(ProfileArguments(app.Family, dataRoot));
            foreach (var (k, v) in ProfileEnvironment(app, dataRoot)) environment[k] = v;
        }
        arguments.AddRange(extraArguments);
        foreach (var (k, v) in extraEnvironment) environment[k] = v;

        if (app.InstallKind == InstallKind.Packaged && app.PackageId is not null)
        {
            // Started by Windows from the AppUserModelId; the shortcut goes through OpenParallel,
            // which asks Windows to start it with these arguments. Windows starts it with its own
            // environment, so environment variables can't be passed.
            return (new LaunchPlan { Target = Windows.PackagedApps.ActivationPrefix + app.PackageId, Arguments = arguments }, "packaged");
        }

        var exe = app.ExePath;
        var workDir = Path.GetDirectoryName(exe);
        if (app.InstallKind == InstallKind.Squirrel && app.UpdateExePath is not null)
        {
            // Update.exe always starts the newest installed version, so vendor updates keep working.
            var exeName = Path.GetFileName(exe);
            var squirrelArgs = new List<string> { "--processStart", exeName };
            if (arguments.Count > 0) squirrelArgs.AddRange(["--process-start-args", WindowsCommandLine.Join(arguments)]);
            var plan = new LaunchPlan { Target = app.UpdateExePath, Arguments = squirrelArgs, Environment = environment, WorkingDirectory = app.InstallRoot };
            return environment.Count == 0 ? (plan, "squirrel") : (plan, "launcher");
        }

        var direct = new LaunchPlan { Target = exe, Arguments = arguments, Environment = environment, WorkingDirectory = workDir };
        return environment.Count == 0 ? (direct, "direct") : (direct, "launcher");
    }

    /// <summary>A website instance: the browser in app mode with its own data folder.</summary>
    public static LaunchPlan WebPlan(string browserExe, string url, string dataRoot) => new()
    {
        Target = browserExe,
        Arguments =
        [
            $"--app={url}",
            $"--user-data-dir={Path.Combine(dataRoot, "web")}",
            "--no-first-run",
            "--no-default-browser-check",
        ],
        WorkingDirectory = Path.GetDirectoryName(browserExe),
    };
}

using System.Text.Json;

namespace OpenParallel.Core;

/// <summary>Creates, finds, starts, recognises and removes instances. All Windows access goes
/// through <see cref="IPlatform"/>.</summary>
public sealed class InstanceService(IPlatform platform)
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Tested exact versions on Windows: (exe name, version) → status. Grows only from
    /// recorded two-copy tests.</summary>
    public static IReadOnlyDictionary<(string, string), CompatibilityStatus> Ledger { get; } =
        new Dictionary<(string, string), CompatibilityStatus>();

    /// <summary>An icon OpenParallel saved for the instance (Store apps), used by its shortcuts.</summary>
    public const string IconFileName = "icon.ico";

    public IPlatform Platform => platform;
    public string InstancesRoot => Path.Combine(platform.DataDirectory, "Instances");
    public string ShortcutsFolder => platform.ShortcutsFolder;
    public string LogsFolder => Path.Combine(platform.DataDirectory, "Logs");

    public string DataRootOf(InstanceManifest manifest) => Path.Combine(InstancesRoot, manifest.InstanceSlug);

    public InspectedApp Inspect(string path) => platform.Apps.Inspect(path);

    // MARK: Reading

    public IReadOnlyList<InstanceManifest> List()
    {
        if (!Directory.Exists(InstancesRoot)) return [];
        var result = new List<InstanceManifest>();
        foreach (var dir in Directory.EnumerateDirectories(InstancesRoot))
        {
            var file = Path.Combine(dir, "instance.json");
            if (!File.Exists(file)) continue;
            try
            {
                var manifest = JsonSerializer.Deserialize<InstanceManifest>(File.ReadAllText(file), Json);
                if (manifest is not null && manifest.Platform == platform.Name) result.Add(manifest);
            }
            catch (JsonException)
            {
                // A broken manifest is skipped, never "repaired": the library only presents.
            }
        }
        return result.OrderBy(m => m.InstanceName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public InstanceManifest? Find(string slug) =>
        List().FirstOrDefault(m => string.Equals(m.InstanceSlug, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>Shortcuts in the OpenParallel Start menu folder that no instance owns.</summary>
    public IReadOnlyList<string> LegacyShortcuts()
    {
        if (!Directory.Exists(ShortcutsFolder)) return [];
        var owned = List().SelectMany(m => new[] { m.ShortcutPath, m.DesktopShortcutPath })
            .Where(p => p is not null).Select(p => Path.GetFullPath(p!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(ShortcutsFolder, platform.ShortcutPattern)
            .Where(p => !owned.Contains(Path.GetFullPath(p)))
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // MARK: Creating

    /// <summary>The name a new instance will get: the requested one, or the first free
    /// <c>Name 2</c>, <c>Name 3</c>… A data folder that still holds files keeps its name
    /// reserved, so a new instance never adopts someone's leftover data; an empty one is reused.</summary>
    public string ReserveName(string requested)
    {
        var baseName = requested.Trim();
        if (baseName.Length == 0) throw new ArgumentException("Instance name required.");
        for (var suffix = 1; ; suffix++)
        {
            var candidate = suffix == 1 ? baseName : $"{baseName} {suffix}";
            var shortcut = Path.Combine(ShortcutsFolder, platform.ShortcutFileName(candidate, Naming.Slug(candidate)));
            var data = Path.Combine(InstancesRoot, Naming.Slug(candidate));
            if (!File.Exists(shortcut) && !HoldsFiles(data)) return candidate;
        }
    }

    private static bool HoldsFiles(string folder) =>
        Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any();

    public InstanceManifest Create(InstanceRequest request, InspectedApp? app = null)
    {
        var isWebsite = request.WebUrl is not null;
        if (isWebsite)
        {
            if (!Uri.TryCreate(request.WebUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https") || uri.Host.Length == 0)
                throw new ArgumentException("A website instance needs an http or https address.");
            if (request.AppPath is null || !File.Exists(request.AppPath))
                throw new ArgumentException("No Chromium browser (Edge, Chrome, Brave) was found to open the website.");
        }
        else
        {
            if (request.AppPath is null || !File.Exists(request.AppPath))
                throw new ArgumentException("Choose an app to copy.");
            app ??= Inspect(request.AppPath);
            if (!app.CanCreate) throw new InvalidOperationException(app.Notes);
        }
        foreach (var key in request.Environment.Keys)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Za-z_][A-Za-z0-9_]*$"))
                throw new ArgumentException($"Invalid environment variable name: {key}");
        }

        var name = ReserveName(request.Name);
        var slug = Naming.Slug(name);
        var dataRoot = Path.Combine(InstancesRoot, slug);
        var shortcutPath = Path.Combine(ShortcutsFolder, platform.ShortcutFileName(name, slug));
        var desktopPath = request.DesktopShortcut ? Path.Combine(platform.DesktopDirectory, platform.ShortcutFileName(name, slug)) : null;
        var createdData = !Directory.Exists(dataRoot);
        var aumid = $"OpenParallel.{slug}";
        string? extraData = null;

        try
        {
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(ShortcutsFolder);

            var knowledge = platform.Apps;
            LaunchPlan plan;
            string mode;
            string family;
            string processFolder;
            bool isolated;
            InspectedApp? browser = null;
            if (isWebsite)
            {
                browser = knowledge.Inspect(request.AppPath!);
                plan = knowledge.WebPlan(browser, request.WebUrl!, dataRoot, slug);
                mode = "web-app";
                family = AppFamily.Website.ToString();
                processFolder = knowledge.ProcessFolder(browser);
                isolated = true;
            }
            else
            {
                isolated = request.SeparateData && app!.SeparatesData;
                extraData = isolated ? knowledge.ExtraDataPath(app!, slug) : null;
                (plan, mode) = knowledge.AppPlan(app!, extraData ?? dataRoot, isolated, request.ExtraArguments, request.Environment, slug, platform.LauncherPath);
                family = app!.Family.ToString();
                processFolder = knowledge.ProcessFolder(app);
                knowledge.Prepare(app, extraData ?? dataRoot, isolated);
            }

            var manifest = new InstanceManifest
            {
                Platform = platform.Name,
                InstanceName = name,
                InstanceSlug = slug,
                Kind = isWebsite ? "website" : "app",
                Family = family,
                LaunchMode = mode,
                SourcePath = isWebsite ? request.WebUrl! : app!.ExePath,
                SourceName = isWebsite ? new Uri(request.WebUrl!).Host : app!.Name,
                SourceVersionAtCreation = isWebsite ? "" : app!.Version,
                ProfileIsolation = isolated,
                WebUrl = request.WebUrl,
                LaunchTarget = plan.Target,
                LaunchArguments = plan.Arguments,
                Environment = plan.Environment,
                WorkingDirectory = plan.WorkingDirectory,
                ProcessFolder = processFolder,
                ShortcutPath = shortcutPath,
                DesktopShortcutPath = desktopPath,
                AppUserModelId = aumid,
                CreatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                IconName = isWebsite ? browser!.IconName : app!.IconName,
                WindowClass = plan.WindowClass,
                ExtraDataPath = extraData,
                OpenedWith = isWebsite ? browser!.Name : null,
                StatusAtCreation = isWebsite ? null : app!.Status,
                StatusNotes = isWebsite ? null : app!.Notes,
            };
            foreach (var path in new[] { shortcutPath, desktopPath }.Where(p => p is not null)) WriteShortcut(manifest, path!);
            var json = JsonSerializer.Serialize(manifest, Json);
            var temp = Path.Combine(dataRoot, "instance.json.tmp");
            File.WriteAllText(temp, json);
            File.Move(temp, Path.Combine(dataRoot, "instance.json"), overwrite: true);
            return manifest;
        }
        catch
        {
            // Undo only what this call made; nothing here existed a moment ago.
            foreach (var path in new[] { shortcutPath, desktopPath }.Where(p => p is not null && File.Exists(p))) File.Delete(path!);
            if (createdData && Directory.Exists(dataRoot)) Directory.Delete(dataRoot, recursive: true);
            if (extraData is not null && Directory.Exists(extraData) && !Directory.EnumerateFileSystemEntries(extraData).Any()) Directory.Delete(extraData);
            throw;
        }
    }

    /// <summary>The shortcut an instance's Start menu and desktop entries hold.</summary>
    private void WriteShortcut(InstanceManifest manifest, string path)
    {
        // "packaged": a Store app starts through Windows, which a shortcut can't ask for.
        var launcher = manifest.LaunchMode is "launcher" or "packaged";
        IReadOnlyList<string> arguments = launcher ? ["--launch", manifest.InstanceSlug] : manifest.LaunchArguments;
        var savedIcon = Path.Combine(DataRootOf(manifest), IconFileName);
        platform.WriteShortcut(new ShortcutDefinition
        {
            Path = path,
            Name = manifest.InstanceName,
            Target = launcher ? platform.LauncherPath : manifest.LaunchTarget,
            Arguments = platform.JoinArguments(arguments),
            ArgumentList = arguments,
            Environment = launcher ? new Dictionary<string, string>() : manifest.Environment,
            WindowClass = manifest.WindowClass,
            WorkingDirectory = manifest.WorkingDirectory,
            IconPath = manifest.IconName ?? (File.Exists(savedIcon) ? savedIcon : manifest.IsWebsite ? manifest.LaunchTarget : manifest.SourcePath),
            Description = manifest.IsWebsite
                ? $"{manifest.InstanceName}: {manifest.WebUrl} in its own window"
                : $"{manifest.InstanceName}: a separate copy of {manifest.SourceName}",
            AppUserModelId = manifest.AppUserModelId,
        });
    }

    /// <summary>Shortcuts of this instance that are no longer where the manifest says.</summary>
    public IReadOnlyList<string> MissingShortcuts(InstanceManifest manifest) =>
        new[] { manifest.ShortcutPath, manifest.DesktopShortcutPath }.Where(p => p is not null && !File.Exists(p)).Select(p => p!).ToList();

    /// <summary>Writes the missing shortcuts again from the manifest. Returns how many.</summary>
    public int RestoreShortcuts(InstanceManifest manifest)
    {
        var missing = MissingShortcuts(manifest);
        foreach (var path in missing)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteShortcut(manifest, path);
        }
        return missing.Count;
    }

    // MARK: Running

    public int Launch(InstanceManifest manifest)
    {
        Directory.CreateDirectory(DataRootOf(manifest));
        // Copies made before the copy's updater was turned off get it turned off here.
        if (manifest.ProfileIsolation && manifest.Family == nameof(AppFamily.VSCode))
            RecipeBook.LeaveUpdatesToTheOriginal(manifest.ExtraDataPath ?? DataRootOf(manifest));
        // And copies made before their profile folder was made up front get it now.
        if (manifest.ProfileIsolation && Enum.TryParse<AppFamily>(manifest.Family, out var family))
            foreach (var folder in RecipeBook.ProfileFolders(family, manifest.ExtraDataPath ?? DataRootOf(manifest))) Directory.CreateDirectory(folder);
        try
        {
            return platform.Start(new LaunchPlan
            {
                Target = manifest.LaunchTarget,
                Arguments = manifest.LaunchArguments,
                Environment = manifest.Environment,
                WorkingDirectory = manifest.WorkingDirectory,
            });
        }
        catch (Exception error)
        {
            Log(manifest.InstanceSlug, $"launch failed: {error.Message}");
            throw;
        }
    }

    /// <summary>Running PIDs per instance slug.</summary>
    public Dictionary<string, List<int>> Running(IReadOnlyList<InstanceManifest>? instances = null)
    {
        if (platform.Matching == ProcessMatching.Arguments)
            return ProcessMatcher.MatchArguments(instances ?? List(), DataRootOf, platform.Processes(), platform.EnvironmentOf);
        var targets = ProcessMatcher.Targets(instances ?? List(), DataRootOf);
        if (targets.Count == 0) return [];
        var processes = platform.Processes();
        var (running, pending) = ProcessMatcher.Match(targets, processes);
        foreach (var pid in pending)
        {
            var environment = platform.EnvironmentOf(pid);
            var image = processes.First(p => p.Pid == pid).ImagePath;
            if (environment is not null) ProcessMatcher.MatchEnvironment(targets, pid, image, environment, running);
        }
        return running;
    }

    public IReadOnlyList<int> RunningPids(InstanceManifest manifest) =>
        Running([manifest]).GetValueOrDefault(manifest.InstanceSlug) ?? [];

    /// <summary>Brings a running instance forward, or starts it.</summary>
    public void OpenOrShow(InstanceManifest manifest)
    {
        var pids = RunningPids(manifest);
        if (pids.Any(platform.BringToFront)) return;
        Launch(manifest);
    }

    /// <summary>Asks the instance to close. Returns false if nothing was running.</summary>
    public bool Quit(InstanceManifest manifest)
    {
        var pids = RunningPids(manifest);
        foreach (var pid in pids) platform.RequestClose(pid);
        return pids.Count > 0;
    }

    public void ForceQuit(InstanceManifest manifest)
    {
        foreach (var pid in RunningPids(manifest)) platform.ForceQuit(pid);
    }

    // MARK: Removing

    /// <summary>Everything that belongs to this instance and only to it.</summary>
    public IReadOnlyList<string> OwnedPaths(InstanceManifest manifest)
    {
        var paths = new List<string?>
        {
            manifest.ShortcutPath,
            manifest.DesktopShortcutPath,
            DataRootOf(manifest),
            manifest.ExtraDataPath,
            Path.Combine(LogsFolder, $"{manifest.InstanceSlug}.log"),
            RedirectedDataRoot(manifest),
        };
        return paths.Where(p => p is not null && (File.Exists(p) || Directory.Exists(p))).Select(p => p!).ToList();
    }

    /// <summary>For a Store app's instance: where Windows would have put the data folder had it
    /// redirected it into the package's storage (normally empty, since the folder is made first).</summary>
    public string? RedirectedDataRoot(InstanceManifest manifest)
    {
        if (!manifest.LaunchTarget.StartsWith(Windows.PackagedApps.ActivationPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var family = manifest.LaunchTarget[Windows.PackagedApps.ActivationPrefix.Length..].Split('!')[0];
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var data = Path.GetFullPath(DataRootOf(manifest));
        if (family.Length == 0 || local.Length == 0 || !data.StartsWith(local + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        return Path.Combine(local, "Packages", family, "LocalCache", "Local", Path.GetRelativePath(local, data));
    }

    /// <summary>Moves the instance to the Recycle Bin. Refuses while it runs.</summary>
    public void MoveToRecycleBin(InstanceManifest manifest)
    {
        var bin = platform.Name == "windows" ? "the Recycle Bin" : "the Trash";
        if (RunningPids(manifest).Count > 0)
            throw new InvalidOperationException($"Quit {manifest.InstanceName} before moving it to {bin}.");
        var paths = OwnedPaths(manifest);
        var root = Path.GetFullPath(InstancesRoot);
        // Only a direct child of the Instances folder is ever removed as a data folder.
        var data = Path.GetFullPath(DataRootOf(manifest));
        if (!string.Equals(Path.GetDirectoryName(data.TrimEnd('\\', '/')), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to remove a folder outside OpenParallel's Instances folder.");
        // Data kept elsewhere (Snaps) must be OpenParallel's own folder for this instance.
        if (manifest.ExtraDataPath is { } extra)
        {
            var full = Path.GetFullPath(extra).TrimEnd('\\', '/');
            if (Path.GetFileName(full) != manifest.InstanceSlug || Path.GetFileName(Path.GetDirectoryName(full)) != "OpenParallel")
                throw new InvalidOperationException("Refusing to remove a data folder OpenParallel did not make.");
        }
        platform.MoveToRecycleBin(paths);
    }

    public void MoveLegacyShortcutToRecycleBin(string path)
    {
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(ShortcutsFolder), StringComparison.OrdinalIgnoreCase)
            || !LegacyShortcuts().Contains(path))
            throw new InvalidOperationException("Only OpenParallel's own leftover shortcuts can be removed here.");
        platform.MoveToRecycleBin([path]);
    }

    /// <summary>The instance folder plus data kept elsewhere (a Snap's copy keeps its profile
    /// inside the snap's own folder).</summary>
    public long SizeOnDisk(InstanceManifest manifest)
    {
        long total = 0;
        foreach (var root in new[] { DataRootOf(manifest), manifest.ExtraDataPath }.OfType<string>().Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                try { total += new FileInfo(file).Length; } catch (IOException) { }
            }
        }
        return total;
    }

    public void Log(string slug, string message)
    {
        try
        {
            Directory.CreateDirectory(LogsFolder);
            File.AppendAllText(Path.Combine(LogsFolder, $"{slug}.log"), $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
    }
}

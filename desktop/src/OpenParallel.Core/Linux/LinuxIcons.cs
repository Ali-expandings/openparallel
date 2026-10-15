namespace OpenParallel.Core.Linux;

/// <summary>Finds a desktop entry's <c>Icon</c> as a PNG file: an absolute path, or a theme name
/// looked up in the desktop's icon theme and the themes it inherits from (Ubuntu: Yaru, then
/// Humanity), then hicolor (the fallback theme every app installs into) and pixmaps.</summary>
public static class LinuxIcons
{
    private static readonly string[] Sizes = ["256x256", "128x128", "512x512", "96x96", "64x64", "48x48", "32x32"];
    private static readonly Lazy<IReadOnlyList<string>> Themes = new(() => ThemeChain(CurrentTheme(), DataRoots()));

    public static string? Find(string icon)
    {
        if (Path.IsPathRooted(icon)) return icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(icon) ? icon : null;
        foreach (var theme in Themes.Value)
        {
            foreach (var root in IconRoots())
            {
                foreach (var size in Sizes)
                {
                    var file = Path.Combine(root, theme, size, "apps", icon + ".png");
                    if (File.Exists(file)) return file;
                }
            }
        }
        foreach (var pixmaps in new[] { "/usr/share/pixmaps", "/usr/local/share/pixmaps" })
        {
            var file = Path.Combine(pixmaps, icon + ".png");
            if (File.Exists(file)) return file;
        }
        return null;
    }

    /// <summary>The theme, then what each theme's <c>index.theme</c> says it inherits, ending in hicolor.</summary>
    internal static IReadOnlyList<string> ThemeChain(string? theme, IEnumerable<string> dataRoots)
    {
        var roots = dataRoots.ToList();
        var chain = new List<string>();
        var queue = new Queue<string>(theme is { Length: > 0 } ? [theme] : []);
        while (queue.Count > 0 && chain.Count < 10)
        {
            var name = queue.Dequeue();
            if (name == "hicolor" || chain.Contains(name)) continue;
            chain.Add(name);
            var index = roots.Select(r => Path.Combine(r, "icons", name, "index.theme")).FirstOrDefault(File.Exists);
            if (index is null) continue;
            var inherits = File.ReadLines(index).FirstOrDefault(l => l.StartsWith("Inherits=", StringComparison.Ordinal));
            foreach (var parent in (inherits?["Inherits=".Length..] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                queue.Enqueue(parent);
        }
        chain.Add("hicolor");
        return chain;
    }

    /// <summary>The desktop's icon theme: GNOME's setting, else GTK's settings file.</summary>
    private static string? CurrentTheme()
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("gsettings", "get org.gnome.desktop.interface icon-theme")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            using var process = System.Diagnostics.Process.Start(info);
            if (process is not null && process.WaitForExit(2000) && process.ExitCode == 0)
            {
                var name = process.StandardOutput.ReadToEnd().Trim().Trim('\'');
                if (name.Length > 0) return name;
            }
        }
        catch (Exception)
        {
            // No gsettings (not GNOME): try GTK's file.
        }
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        var settings = Path.Combine(config, "gtk-3.0", "settings.ini");
        if (!File.Exists(settings)) return null;
        var line = File.ReadLines(settings).FirstOrDefault(l => l.Replace(" ", "").StartsWith("gtk-icon-theme-name=", StringComparison.Ordinal));
        return line?.Split('=', 2)[1].Trim();
    }

    private static IEnumerable<string> DataRoots() =>
        LinuxAppKnowledge.ApplicationFolders().Select(f => Path.GetDirectoryName(f)!).Distinct();

    /// <summary>Where themes live: ~/.icons, then each data folder's icons.</summary>
    private static IEnumerable<string> IconRoots() =>
        new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".icons") }
            .Concat(DataRoots().Select(r => Path.Combine(r, "icons")));
}

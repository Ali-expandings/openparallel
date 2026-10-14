namespace OpenParallel.Core.Linux;

/// <summary>Finds a desktop entry's <c>Icon</c> as a PNG file: an absolute path, or a theme name
/// looked up in hicolor (the fallback theme every app installs into) and pixmaps.</summary>
public static class LinuxIcons
{
    private static readonly string[] Sizes = ["256x256", "128x128", "512x512", "96x96", "64x64", "48x48", "32x32"];

    public static string? Find(string icon)
    {
        if (Path.IsPathRooted(icon)) return icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(icon) ? icon : null;
        foreach (var root in DataRoots())
        {
            foreach (var size in Sizes)
            {
                var file = Path.Combine(root, "icons", "hicolor", size, "apps", icon + ".png");
                if (File.Exists(file)) return file;
            }
        }
        foreach (var pixmaps in new[] { "/usr/share/pixmaps", "/usr/local/share/pixmaps" })
        {
            var file = Path.Combine(pixmaps, icon + ".png");
            if (File.Exists(file)) return file;
        }
        return null;
    }

    private static IEnumerable<string> DataRoots() =>
        LinuxAppKnowledge.ApplicationFolders().Select(f => Path.GetDirectoryName(f)!).Distinct();
}

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Xml;
using System.Xml.Linq;

namespace OpenParallel.Core.Windows;

/// <summary>Microsoft Store (MSIX) desktop apps. Their files sit in a read-only package folder
/// under <c>WindowsApps</c>, and they must be started through Windows, by their
/// AppUserModelId, to run as themselves. Arguments still reach the app, so an Electron app's
/// copy can be given a data folder of its own.</summary>
public static class PackagedApps
{
    /// <summary>Launch targets of Store apps are written as <c>shell:AppsFolder\&lt;AUMID&gt;</c>,
    /// which stays valid when the app updates (the package folder changes, the id doesn't).</summary>
    public const string ActivationPrefix = @"shell:AppsFolder\";

    public sealed record Package(string Root, string FamilyName, string Name, string AppId, string? DisplayName, string? Logo)
    {
        public string AppUserModelId => $"{FamilyName}!{AppId}";
    }

    /// <summary>Store apps that can't be copied but have a website that can.</summary>
    private static readonly Dictionary<string, string> WebAlternatives = new(StringComparer.OrdinalIgnoreCase)
    {
        ["5319275A.WhatsAppDesktop"] = "https://web.whatsapp.com",
        ["SpotifyAB.SpotifyMusic"] = "https://open.spotify.com",
        ["TelegramMessengerLLP.TelegramDesktop"] = "https://web.telegram.org/a/",
        ["MSTeams"] = "https://teams.microsoft.com",
        ["Claude"] = "https://claude.ai",
        ["Anthropic.Claude"] = "https://claude.ai",
    };

    public static string? WebAlternative(string packageName) => WebAlternatives.GetValueOrDefault(packageName);

    /// <summary>The package an exe inside <c>WindowsApps</c> belongs to, or null when the path
    /// isn't in a package or its manifest can't be read.</summary>
    public static Package? Find(string exePath)
    {
        var root = PackageRoot(exePath);
        if (root is null) return null;
        try
        {
            var manifest = Path.Combine(root, "AppxManifest.xml");
            return File.Exists(manifest) ? FromManifest(root, File.ReadAllText(manifest), Path.GetRelativePath(root, exePath)) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }

    /// <summary>The package folder: the one directly inside a folder named <c>WindowsApps</c>.</summary>
    public static string? PackageRoot(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath);
        while (!string.IsNullOrEmpty(dir))
        {
            var parent = Path.GetDirectoryName(dir);
            if (parent is not null && Path.GetFileName(parent).Equals("WindowsApps", StringComparison.OrdinalIgnoreCase)) return dir;
            dir = parent;
        }
        return null;
    }

    /// <summary><c>Name_Version_Arch_Resource_PublisherId</c> → <c>Name_PublisherId</c>.</summary>
    public static string? FamilyName(string packageFolder)
    {
        var parts = packageFolder.Split('_');
        return parts.Length >= 3 && parts[0].Length > 0 && parts[^1].Length > 0 ? $"{parts[0]}_{parts[^1]}" : null;
    }

    /// <summary>Reads the identity and the application entry whose executable is
    /// <paramref name="relativeExe"/> (or the only one).</summary>
    public static Package? FromManifest(string root, string xml, string relativeExe)
    {
        var document = XDocument.Parse(xml);
        var name = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity")?.Attribute("Name")?.Value;
        var family = FamilyName(Path.GetFileName(root));
        if (name is null || family is null) return null;

        var wanted = relativeExe.Replace('/', '\\');
        var applications = document.Descendants().Where(e => e.Name.LocalName == "Application").ToList();
        var application = applications.FirstOrDefault(a =>
            string.Equals(a.Attribute("Executable")?.Value.Replace('/', '\\'), wanted, StringComparison.OrdinalIgnoreCase))
            ?? (applications.Count == 1 ? applications[0] : null);
        var id = application?.Attribute("Id")?.Value;
        if (id is null) return null;

        var visual = application!.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
        var display = visual?.Attribute("DisplayName")?.Value;
        if (display is not null && display.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) display = null;
        var logo = visual?.Attribute("Square44x44Logo")?.Value ?? visual?.Attribute("Square150x150Logo")?.Value;
        return new Package(root, family, name, id, display, logo);
    }

    // MARK: Listing

    /// <summary>A Start menu entry of an installed Store package.</summary>
    public sealed record ListedApp(string Name, string Exe, string Family, string AppId, Version Version);

    /// <summary>Windows' own packages (Notepad, Terminal, the Store, WSL, frameworks): not apps to copy.</summary>
    public static bool IsWindowsOwn(string packageName) =>
        packageName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
        || packageName.StartsWith("Windows.", StringComparison.OrdinalIgnoreCase);

    /// <summary>The desktop apps a package shows in the Start menu. Helpers hidden from it
    /// (Claude's SSH tools), UWP apps (no exe of their own to start), frameworks, resource
    /// packages and Windows' own packages are left out.</summary>
    public static IReadOnlyList<ListedApp> ListedApps(string root, string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return [];
        }
        var all = document.Descendants().ToList();
        var identity = all.FirstOrDefault(e => e.Name.LocalName == "Identity");
        var name = identity?.Attribute("Name")?.Value;
        var folder = root.TrimEnd('\\', '/');
        var family = FamilyName(folder[(folder.LastIndexOfAny(['\\', '/']) + 1)..]);
        if (string.IsNullOrWhiteSpace(name) || family is null || IsWindowsOwn(name)) return [];

        string? Property(string element) =>
            all.FirstOrDefault(e => e.Name.LocalName == element && e.Parent?.Name.LocalName == "Properties")?.Value.Trim();
        if (Property("Framework") == "true" || Property("ResourcePackage") == "true") return [];
        var version = Version.TryParse(identity!.Attribute("Version")?.Value, out var parsed) ? parsed : new Version(0, 0);

        var listed = new List<ListedApp>();
        foreach (var application in all.Where(e => e.Name.LocalName == "Application"))
        {
            var exe = application.Attribute("Executable")?.Value;
            var id = application.Attribute("Id")?.Value;
            if (exe is null || id is null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            var fullTrust = application.Attribute("EntryPoint")?.Value == "Windows.FullTrustApplication"
                || application.Attributes().Any(a => a.Name.LocalName == "RuntimeBehavior" && a.Value is "packagedClassicApp" or "win32App");
            if (!fullTrust) continue;
            var visual = application.Elements().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
            if (string.Equals(visual?.Attribute("AppListEntry")?.Value, "none", StringComparison.OrdinalIgnoreCase)) continue;
            var display = new[] { visual?.Attribute("DisplayName")?.Value, Property("DisplayName"), name }
                .First(n => !string.IsNullOrWhiteSpace(n) && !n.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))!;
            listed.Add(new ListedApp(display, Path.Combine(root, exe.Replace('\\', Path.DirectorySeparatorChar)), family, id, version));
        }
        return listed;
    }

    // MARK: Icon

    /// <summary>Writes the app's largest list logo as an .ico for the copy's shortcut. The
    /// package folder changes with every update, so the shortcut can't point into it.</summary>
    public static bool WriteIcon(string exePath, string icoPath)
    {
        try
        {
            var package = Find(exePath);
            var png = package is null ? null : LargestLogo(package);
            if (png is null) return false;
            WriteIco(File.ReadAllBytes(png), icoPath);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The biggest PNG among the logo's scale and size variants, preferring the
    /// transparent ("unplated") ones and avoiding the light-theme glyphs.</summary>
    public static string? LargestLogo(Package package)
    {
        if (package.Logo is null) return null;
        var relative = package.Logo.Replace('\\', Path.DirectorySeparatorChar);
        var folder = Path.Combine(package.Root, Path.GetDirectoryName(relative) ?? "");
        var stem = Path.GetFileNameWithoutExtension(relative);
        if (!Directory.Exists(folder)) return null;
        return Directory.EnumerateFiles(folder, stem + "*.png")
            .Select(file => (File: file, Width: PngWidth(file), Name: Path.GetFileName(file)))
            .Where(c => c.Width > 0 && !c.Name.Contains("lightunplated", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => Math.Min(c.Width, 256))
            .ThenByDescending(c => c.Name.Contains("unplated", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.File)
            .FirstOrDefault();
    }

    private static int PngWidth(string file)
    {
        Span<byte> header = stackalloc byte[24];
        using var stream = File.OpenRead(file);
        if (stream.Read(header) < 24 || header[0] != 0x89 || header[1] != (byte)'P') return 0;
        return BinaryPrimitives.ReadInt32BigEndian(header[16..]);
    }

    /// <summary>A one-image .ico holding the PNG as is (Windows Vista and later read these).</summary>
    public static void WriteIco(byte[] png, string icoPath)
    {
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        using var writer = new BinaryWriter(File.Create(icoPath));
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write((byte)(width >= 256 ? 0 : width));
        writer.Write((byte)(height >= 256 ? 0 : height));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(png.Length);
        writer.Write(6 + 16);
        writer.Write(png);
    }

    // MARK: Starting

    /// <summary>Starts a Store app as itself, with <paramref name="arguments"/> on its command
    /// line, and returns its process id. Created out of process, as Microsoft asks of callers
    /// that exit right after launching (OpenParallel's <c>--launch</c>).</summary>
    [SupportedOSPlatform("windows")]
    public static int Activate(string appUserModelId, string arguments)
    {
        CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
        var clsid = new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C");
        var iid = typeof(IApplicationActivationManager).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_LOCAL_SERVER, ref iid, out var instance));
        var manager = (IApplicationActivationManager)instance;
        try
        {
            manager.ActivateApplication(appUserModelId, arguments, 0, out var processId);
            return (int)processId;
        }
        finally
        {
            Marshal.ReleaseComObject(manager);
        }
    }

    private const uint CLSCTX_LOCAL_SERVER = 4;
    private const uint COINIT_APARTMENTTHREADED = 2;

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object instance);

    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, int options, out uint processId);
    }
}

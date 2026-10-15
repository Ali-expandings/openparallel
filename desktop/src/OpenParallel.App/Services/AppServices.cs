using System.Diagnostics;
using System.Text.Json;
using Avalonia.Media.Imaging;
using FluentAvalonia.UI.Controls;
using OpenParallel.Core;

namespace OpenParallel.App.Services;

/// <summary>Preferences, stored as JSON in %LOCALAPPDATA%\OpenParallel.</summary>
public sealed class AppSettings
{
    /// <summary><c>System</c>, <c>Light</c> or <c>Dark</c>.</summary>
    public string Theme { get; set; } = "System";
    /// <summary>Closing the window leaves OpenParallel in the notification area.</summary>
    public bool KeepInNotificationArea { get; set; }
    public bool OpenAfterCreating { get; set; } = true;
    public bool DesktopShortcut { get; set; }

    /// <summary>Windows: %LOCALAPPDATA%\OpenParallel. Linux: ~/.config/OpenParallel (XDG config).</summary>
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(OperatingSystem.IsWindows() ? Environment.SpecialFolder.LocalApplicationData : Environment.SpecialFolder.ApplicationData),
        "OpenParallel", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), InstanceService.Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, InstanceService.Json));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>App icons from exe files, cached. Never throws: a missing icon is just null.</summary>
public static class AppIcons
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock Gate = new();

    /// <summary>An app's icon: from its exe on Windows, from its desktop entry's icon on Linux.</summary>
    public static Bitmap? For(InspectedApp app) => OperatingSystem.IsWindows() ? For(app.ExePath) : FromIconName(app.IconName);

    /// <summary>Linux: a theme icon name or file, as a PNG from the icon folders (SVG-only icons fall back to a symbol).</summary>
    public static Bitmap? FromIconName(string? icon)
    {
        if (string.IsNullOrEmpty(icon)) return null;
        lock (Gate)
        {
            if (Cache.TryGetValue("icon:" + icon, out var cached)) return cached;
        }
        Bitmap? bitmap = null;
        try
        {
            var file = OpenParallel.Core.Linux.LinuxIcons.Find(icon);
            if (file is not null) bitmap = new Bitmap(file);
        }
        catch (Exception)
        {
            bitmap = null;
        }
        lock (Gate)
        {
            Cache["icon:" + icon] = bitmap;
        }
        return bitmap;
    }

    public static Bitmap? For(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !OperatingSystem.IsWindows()) return null;
        lock (Gate)
        {
            if (Cache.TryGetValue(exePath, out var cached)) return cached;
        }
        Bitmap? bitmap = null;
        try
        {
            if (File.Exists(exePath))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon is not null)
                {
                    using var image = icon.ToBitmap();
                    using var stream = new MemoryStream();
                    image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    stream.Position = 0;
                    bitmap = new Bitmap(stream);
                }
            }
        }
        catch (Exception)
        {
            bitmap = null;
        }
        lock (Gate)
        {
            Cache[exePath] = bitmap;
        }
        return bitmap;
    }

    /// <summary>The icon of an instance: its app, or for websites the browser it opens in.</summary>
    public static Bitmap? For(InstanceManifest manifest) =>
        !OperatingSystem.IsWindows() ? (manifest.IsWebsite ? null : FromIconName(manifest.IconName))
        : manifest.IsWebsite ? null : For(manifest.SourcePath) ?? For(manifest.LaunchTarget);

    public static FAIconSource IconSource(InstanceManifest manifest)
    {
        var bitmap = For(manifest);
        if (bitmap is not null) return new FAImageIconSource { Source = bitmap };
        return new FASymbolIconSource { Symbol = manifest.IsWebsite ? FASymbol.Globe : FASymbol.Document };
    }

    public static void Forget() { lock (Gate) Cache.Clear(); }
}

/// <summary>What a status means to the person using the app. Same wording as the Mac app:
/// exactly what was shown, nothing more.</summary>
public sealed record StatusText(string Title, FASymbol Symbol, string Detail)
{
    public static readonly StatusText Website = new("Website", FASymbol.Globe,
        "Opens in its own window with its own cookies and storage, separate from your browser and from every other website instance.");

    public static StatusText For(InspectedApp app) => app.Status switch
    {
        CompatibilityStatus.Verified => new("Verified", FASymbol.StarFilled,
            "A real sign-in and sign-out test confirmed this app keeps accounts apart."),
        CompatibilityStatus.Tested => new("Tested", FASymbol.Checkmark,
            "This exact version ran as two separate copies with separate data on a test machine. Signing in to two different accounts has not been checked yet."),
        CompatibilityStatus.Supported => new("Supported", FASymbol.Checkmark,
            "OpenParallel knows how to give this kind of app its own data folder. This exact version has not been tested yet."),
        CompatibilityStatus.Experimental => new("Experimental", FASymbol.Important, app.Notes),
        CompatibilityStatus.SharesData => new("Shares data", FASymbol.Copy, app.Notes),
        _ => new("Not supported", FASymbol.Dismiss, app.Notes),
    };

    /// <summary>For an existing instance, from what its manifest recorded.</summary>
    public static StatusText For(InstanceManifest manifest)
    {
        if (manifest.IsWebsite) return Website;
        if (!manifest.ProfileIsolation)
            return new("Shares data", FASymbol.Copy, $"This instance opens {manifest.SourceName} again with the same login and data as the original.");
        if (manifest.StatusAtCreation is { } recorded and not CompatibilityStatus.NotSupported and not CompatibilityStatus.SharesData)
            return recorded == CompatibilityStatus.Experimental
                ? new("Experimental", FASymbol.Important, manifest.StatusNotes ?? "Test it before relying on it.")
                : For(new InspectedApp { ExePath = manifest.SourcePath, Name = manifest.SourceName, Family = AppFamily.Generic, Status = recorded, Notes = manifest.StatusNotes ?? "" });
        if (Enum.TryParse<AppFamily>(manifest.Family, out var family) && family == AppFamily.JetBrains)
            return new("Experimental", FASymbol.Important, "Its settings, plugins and caches live in this instance's own folders. Test it before relying on it.");
        // Instances made before the status was saved with them.
        var experimental = manifest.LaunchMode switch
        {
            "packaged" => "A Microsoft Store app built on Electron. It starts through Windows with a data folder of its own. New on Windows: test before relying on it.",
            "snap" => "A Snap: its data goes into the snap’s own folder. Test it before relying on it.",
            "flatpak" => "A Flatpak: the sandbox is given just this instance’s folder. Test it before relying on it.",
            _ when manifest.Family == nameof(AppFamily.Generic) && manifest.Platform == "linux" =>
                "Gets its own settings and data folders (XDG). Most Linux apps keep their data there; some use the original’s anyway. Test before relying on it.",
            _ => null,
        };
        if (experimental is not null) return new("Experimental", FASymbol.Important, experimental);
        return new("Supported", FASymbol.Checkmark,
            "OpenParallel knows how to give this kind of app its own data folder. This exact version has not been tested yet.");
    }
}

/// <summary>The groups an app falls into in the New instance list, best first.</summary>
public enum Support
{
    Separate,
    SharedData,
    Unsupported,
}

public static class SupportText
{
    public static Support Of(InspectedApp app) =>
        !app.CanCreate ? Support.Unsupported : app.SeparatesData ? Support.Separate : Support.SharedData;

    public static string Title(Support support) => support switch
    {
        Support.Separate => "Separate login and data",
        Support.SharedData => "Opens again, shares data",
        _ => "Not supported",
    };
}

/// <summary>The file manager: File Explorer on Windows, Files (or whatever answers) on Linux.</summary>
public static class Explorer
{
    public static void Show(string path) => Library.Shared.Service.Platform.Reveal(path);

    /// <summary>Opens a file with its default app (logs in Notepad or the text editor).</summary>
    public static void OpenFile(string path) => Library.Shared.Service.Platform.OpenWithDefaultApp(path);

    /// <summary>Opens a link (mailto:, https:) with the system's default handler.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else
            {
                var info = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                info.ArgumentList.Add(url);
                Process.Start(info);
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>A path with the user's home shortened (%USERPROFILE% on Windows, ~ on Linux).</summary>
    public static string Friendly(string path)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var home = OperatingSystem.IsWindows() ? "%USERPROFILE%" : "~";
        return profile.Length > 0 && path.StartsWith(profile, StringComparison.OrdinalIgnoreCase) ? home + path[profile.Length..] : path;
    }
}

/// <summary>The few words that differ between Windows and Linux.</summary>
public static class Words
{
    private static readonly bool Windows = OperatingSystem.IsWindows();
    public static string Bin => Windows ? "Recycle Bin" : "Trash";
    public static string TheBin => Windows ? "the Recycle Bin" : "the Trash";
    public static string Menu => Windows ? "Start menu" : "applications menu";
    public static string FileManager => Windows ? "File Explorer" : "Files";
    public static string Tray => Windows ? "notification area" : "system tray";
    public static string MenuShortcutAlways => Windows
        ? "A Start menu shortcut is always made, in the OpenParallel folder."
        : "It always appears in the applications menu too.";
    public static string WhereShortcutIs(bool desktop) => Windows
        ? desktop ? "in the Start menu and on your desktop" : "in the Start menu, in the OpenParallel folder"
        : desktop ? "in the applications menu and on your desktop" : "in the applications menu";
}

public static class Format
{
    public static string Size(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }
        return unit == 0 ? $"{bytes} bytes" : $"{value:0.#} {units[unit]}";
    }

    public static string Date(string iso) =>
        DateTimeOffset.TryParse(iso, out var date) ? date.ToLocalTime().ToString("d MMM yyyy, HH:mm") : iso;

    /// <summary>The name a browser shows for itself, e.g. "Microsoft Edge".</summary>
    public static string ProgramName(string exePath)
    {
        if (!OperatingSystem.IsWindows()) return Path.GetFileNameWithoutExtension(exePath);
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription.Trim();
            if (!string.IsNullOrWhiteSpace(info.ProductName)) return info.ProductName.Trim();
        }
        catch (Exception)
        {
        }
        return Path.GetFileNameWithoutExtension(exePath);
    }
}

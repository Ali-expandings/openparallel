using Avalonia.Threading;
using OpenParallel.Core;
using OpenParallel.Core.Windows;

namespace OpenParallel.App.Services;

/// <summary>An installed app as the New instance list shows it.</summary>
public sealed record AppChoice(string Name, InspectedApp App)
{
    public Support Support => SupportText.Of(App);
}

/// <summary>The app's one view of the instances on disk and which of them are running.
/// Everything here is read on demand; nothing runs while OpenParallel is closed.</summary>
public sealed class Library
{
    public static Library Shared { get; } = new();

    public InstanceService Service { get; } = new(OperatingSystem.IsWindows() ? new WindowsPlatform() : new OpenParallel.Core.Linux.LinuxPlatform());
    public AppSettings Settings { get; } = AppSettings.Load();

    public IReadOnlyList<InstanceManifest> Instances { get; private set; } = [];
    public IReadOnlyList<string> LegacyShortcuts { get; private set; } = [];
    public IReadOnlyDictionary<string, List<int>> Running { get; private set; } = new Dictionary<string, List<int>>();

    /// <summary>The list on disk changed (created, removed, refreshed).</summary>
    public event Action? InstancesChanged;
    /// <summary>An instance started or stopped.</summary>
    public event Action? RunningChanged;

    private bool checking;
    private IReadOnlyList<AppChoice>? installed;

    public bool IsRunning(InstanceManifest manifest) =>
        Running.TryGetValue(manifest.InstanceSlug, out var pids) && pids.Count > 0;

    public InstanceManifest? Find(string slug) =>
        Instances.FirstOrDefault(m => string.Equals(m.InstanceSlug, slug, StringComparison.OrdinalIgnoreCase));

    public void Reload()
    {
        try
        {
            Instances = Service.List();
            LegacyShortcuts = Service.LegacyShortcuts();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Instances = [];
            LegacyShortcuts = [];
        }
        InstancesChanged?.Invoke();
        _ = CheckRunningAsync();
    }

    /// <summary>Looks at the running processes once, off the UI thread.</summary>
    public async Task CheckRunningAsync()
    {
        if (checking) return;
        checking = true;
        try
        {
            var instances = Instances;
            var result = await Task.Run(() =>
            {
                try { return Service.Running(instances); }
                catch (Exception) { return null; }
            });
            if (result is null) return;
            var changed = result.Count != Running.Count
                || result.Any(pair => !Running.TryGetValue(pair.Key, out var old) || !old.SequenceEqual(pair.Value));
            Running = result;
            if (changed) RunningChanged?.Invoke();
        }
        finally
        {
            checking = false;
        }
    }

    /// <summary>Checks again shortly, so a just-started or just-quit instance shows quickly.</summary>
    public void CheckSoon()
    {
        foreach (var delay in new[] { 900, 2500, 5000 })
            DispatcherTimer.RunOnce(() => _ = CheckRunningAsync(), TimeSpan.FromMilliseconds(delay));
    }

    /// <summary>Installed apps with what an instance of each would get. Cached until <paramref name="refresh"/>.</summary>
    public async Task<IReadOnlyList<AppChoice>> InstalledAppsAsync(bool refresh = false)
    {
        if (installed is not null && !refresh) return installed;
        var service = Service;
        installed = await Task.Run(() =>
        {
            var apps = new List<AppChoice>();
            foreach (var app in service.Platform.Apps.InstalledApps(service.Platform))
            {
                try
                {
                    var inspected = service.Inspect(app.Path);
                    apps.Add(new AppChoice(app.Name, inspected));
                    AppIcons.For(inspected); // warm the cache off the UI thread
                }
                catch (Exception) { }
            }
            return (IReadOnlyList<AppChoice>)apps;
        });
        return installed;
    }

    public void ForgetInstalledApps() => installed = null;
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using FluentAvalonia.UI.Controls;
using OpenParallel.App.Services;

namespace OpenParallel.App;

public partial class App : Application
{
    public static FontFamily Monospace { get; } = new("Cascadia Mono, Consolas, DejaVu Sans Mono, Ubuntu Mono, Liberation Mono, monospace");

    private MainWindow? window;
    private TrayIcon? tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ApplyTheme(Library.Shared.Settings.Theme);
            window = new MainWindow();
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            Library.Shared.InstancesChanged += UpdateTray;
            Library.Shared.RunningChanged += UpdateTray;
            UpdateTray();
            var shown = window;
            OneInstance.ShowRequested += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => shown.ShowFromTray());
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(string theme) =>
        Current!.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

    // MARK: Notification area

    /// <summary>Shows or removes the notification-area icon to match the setting.</summary>
    public void UpdateTray()
    {
        if (!Library.Shared.Settings.KeepInNotificationArea)
        {
            if (tray is null) return;
            tray.IsVisible = false;
            TrayIcon.SetIcons(this, null);
            tray.Dispose();
            tray = null;
            return;
        }
        if (tray is null)
        {
            using var stream = AssetLoader.Open(new Uri("avares://OpenParallel/Assets/OpenParallel.ico"));
            tray = new TrayIcon { Icon = new WindowIcon(stream), ToolTipText = "OpenParallel" };
            tray.Clicked += (_, _) => window?.ShowFromTray();
            TrayIcon.SetIcons(this, [tray]);
        }
        tray.Menu = TrayMenu();
    }

    private NativeMenu TrayMenu()
    {
        var library = Library.Shared;
        var menu = new NativeMenu();
        void Add(string header, Action action, bool? check = null)
        {
            var item = new NativeMenuItem(header);
            if (check is { } running)
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = running;
            }
            item.Click += (_, _) => action();
            menu.Add(item);
        }

        // Ticked instances are running; choosing one opens it or brings it forward.
        foreach (var m in library.Instances.Take(25))
            Add(m.InstanceName, () => window?.Open(m), library.IsRunning(m));
        if (library.Instances.Count > 0) menu.Add(new NativeMenuItemSeparator());
        Add("New instance…", () =>
        {
            window?.ShowFromTray();
            window?.ShowNewInstance(website: false);
        });
        Add("Open OpenParallel", () => window?.ShowFromTray());
        Add("Settings", () =>
        {
            window?.ShowFromTray();
            window?.ShowSettings();
        });
        menu.Add(new NativeMenuItemSeparator());
        Add("Quit OpenParallel", () =>
        {
            if (window is not null) window.ExitRequested = true;
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        });
        return menu;
    }

    // MARK: Small helpers shared by the pages

    /// <summary>A theme brush, resolved now (use <see cref="Dynamic"/> for one that follows theme changes).</summary>
    public static IBrush Brush(string key, IBrush fallback) =>
        Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;

    /// <summary>Binds a property to a theme resource so it follows light/dark changes.</summary>
    public static void Dynamic(StyledElement target, AvaloniaProperty property, string key) =>
        target.Bind(property, target.GetResourceObservable(key));

    public static FASymbolIconSource Symbol(FASymbol symbol) => new() { Symbol = symbol };

    public static TextBlock Secondary(string text) => new()
    {
        Text = text,
        Classes = { "secondary" },
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 360,
    };
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using OpenParallel.App.Services;
using OpenParallel.App.Views;
using OpenParallel.Core;

namespace OpenParallel.App;

/// <summary>A page that wants the first look at key presses (the new-instance flow).</summary>
public interface IKeyAware
{
    bool HandleKey(KeyEventArgs e, bool typing);
}

public partial class MainWindow : Window
{
    private const string InstanceTag = "instance:";
    private const string LegacyTag = "legacy:";

    private readonly Library library = Library.Shared;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private DispatcherTimer? bannerTimer;
    /// <summary>What the content area shows: instance:&lt;slug&gt;, legacy:&lt;path&gt;, new, settings or empty.</summary>
    private string? route;
    private bool syncing;
    private bool dialogOpen;

    /// <summary>Set by the notification-area menu so closing really quits.</summary>
    public bool ExitRequested { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        Nav.SelectionChanged += OnNavSelectionChanged;
        ReportItem.IconSource = App.Symbol(FASymbol.Help);
        NewButton.Click += (_, _) => ShowNewInstance(website: false);
        Search.TextChanged += (_, _) => RebuildNav();
        Search.KeyDown += OnSearchKey;
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        library.InstancesChanged += RebuildNav;
        library.RunningChanged += OnRunningChanged;
        // Running state is only looked at while the window is on screen.
        timer.Tick += (_, _) =>
        {
            if (IsVisible && WindowState != WindowState.Minimized) _ = library.CheckRunningAsync();
        };
        Activated += (_, _) => _ = library.CheckRunningAsync();
        Opened += (_, _) => timer.Start();
        Closing += OnClosing;
        library.Reload();
    }

    public InstanceManifest? SelectedInstance =>
        route?.StartsWith(InstanceTag, StringComparison.Ordinal) == true ? library.Find(route[InstanceTag.Length..]) : null;

    // MARK: Sidebar

    private static IEnumerable<(string Title, List<InstanceManifest> Items)> Groups(IEnumerable<InstanceManifest> instances)
    {
        var list = instances.ToList();
        foreach (var group in list.Where(m => !m.IsWebsite)
                     .GroupBy(m => m.SourceName.Length > 0 ? m.SourceName : "Apps", StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            yield return (group.Key, group.ToList());
        var websites = list.Where(m => m.IsWebsite).ToList();
        if (websites.Count > 0) yield return ("Websites", websites);
    }

    private static bool Matches(InstanceManifest m, string query) =>
        query.Length == 0
        || m.InstanceName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || m.SourceName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || (m.WebUrl?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    private void RebuildNav()
    {
        var query = (Search.Text ?? "").Trim();
        var items = new List<object>();
        foreach (var (title, group) in Groups(library.Instances.Where(m => Matches(m, query))))
        {
            items.Add(new FANavigationViewItemHeader { Content = title });
            items.AddRange(group.Select(ItemFor));
        }
        var legacy = library.LegacyShortcuts
            .Where(p => query.Length == 0 || Path.GetFileNameWithoutExtension(p).Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        if (legacy.Count > 0)
        {
            items.Add(new FANavigationViewItemHeader { Content = "Older shortcuts" });
            items.AddRange(legacy.Select(LegacyItemFor));
        }
        if (items.Count == 0 && query.Length > 0) items.Add(new FANavigationViewItemHeader { Content = "No matching instances" });

        syncing = true;
        Nav.MenuItems.Clear();
        foreach (var item in items) Nav.MenuItems.Add(item);
        syncing = false;

        var gone = route is null or "empty"
            || (route.StartsWith(InstanceTag, StringComparison.Ordinal) && SelectedInstance is null)
            || (route.StartsWith(LegacyTag, StringComparison.Ordinal) && !library.LegacyShortcuts.Contains(route[LegacyTag.Length..]));
        if (gone) ShowDefault();
        else SyncSelection();
    }

    private FANavigationViewItem ItemFor(InstanceManifest m)
    {
        var item = new FANavigationViewItem
        {
            Content = m.InstanceName,
            Tag = InstanceTag + m.InstanceSlug,
            IconSource = AppIcons.IconSource(m),
        };
        AutomationProperties.SetName(item, m.InstanceName);
        item.AddHandler(DoubleTappedEvent, (_, _) => Open(m), RoutingStrategies.Bubble, handledEventsToo: true);
        item.ContextFlyout = MenuFor(m);
        SetBadge(item, library.IsRunning(m));
        return item;
    }

    private FANavigationViewItem LegacyItemFor(string path)
    {
        var item = new FANavigationViewItem
        {
            Content = Path.GetFileNameWithoutExtension(path),
            Tag = LegacyTag + path,
            IconSource = new FASymbolIconSource { Symbol = FASymbol.Link },
        };
        return item;
    }

    private void SetBadge(FANavigationViewItem item, bool running)
    {
        if (running == (item.InfoBadge is not null)) return;
        if (!running)
        {
            item.InfoBadge = null;
            return;
        }
        var badge = new FAInfoBadge();
        App.Dynamic(badge, Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, "SystemFillColorSuccessBrush");
        ToolTip.SetTip(badge, "Running");
        AutomationProperties.SetName(badge, "Running");
        item.InfoBadge = badge;
    }

    private MenuFlyout MenuFor(InstanceManifest m)
    {
        // No shortcut hints here: Windows context menus don't show them (and Wine clips them).
        var open = new MenuItem { Header = "Open" };
        var quit = new MenuItem { Header = "Quit" };
        var force = new MenuItem { Header = "Force quit" };
        var folder = new MenuItem { Header = "Open data folder" };
        var shortcut = new MenuItem { Header = "Open shortcut location" };
        var trash = new MenuItem { Header = $"Move to {Words.Bin}…" };
        open.Click += (_, _) => Open(m);
        quit.Click += (_, _) => Quit(m);
        force.Click += (_, _) => Quit(m, force: true);
        folder.Click += (_, _) => Explorer.Show(library.Service.DataRootOf(m));
        shortcut.Click += (_, _) => Explorer.Show(m.ShortcutPath);
        trash.Click += (_, _) => _ = ConfirmMoveToRecycleBin(m);
        var flyout = new MenuFlyout { Items = { open, quit, force, new Separator(), folder, shortcut, new Separator(), trash } };
        flyout.Opening += (_, _) =>
        {
            var running = library.IsRunning(m);
            open.Header = running ? "Show" : "Open";
            quit.IsVisible = running;
            force.IsVisible = running;
            trash.IsEnabled = !running;
        };
        return flyout;
    }

    private void OnRunningChanged()
    {
        foreach (var item in Nav.MenuItems.OfType<FANavigationViewItem>())
        {
            if (item.Tag is string tag && tag.StartsWith(InstanceTag, StringComparison.Ordinal) && library.Find(tag[InstanceTag.Length..]) is { } m)
                SetBadge(item, library.IsRunning(m));
        }
        (PageHost.Content as InstancePage)?.UpdateRunning();
    }

    private void SyncSelection()
    {
        syncing = true;
        try
        {
            if (route == "settings")
            {
                Nav.SelectedItem = Nav.SettingsItem;
                return;
            }
            if (route == "report")
            {
                Nav.SelectedItem = ReportItem;
                return;
            }
            Nav.SelectedItem = Nav.MenuItems.OfType<FANavigationViewItem>().FirstOrDefault(i => Equals(i.Tag, route));
        }
        finally
        {
            syncing = false;
        }
    }

    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (syncing) return;
        if (e.IsSettingsSelected)
        {
            ShowSettings();
            return;
        }
        if (e.SelectedItem is not FANavigationViewItem { Tag: string tag }) return;
        if (tag == "report")
        {
            ShowReport();
            return;
        }
        if (tag.StartsWith(InstanceTag, StringComparison.Ordinal) && library.Find(tag[InstanceTag.Length..]) is { } m) ShowInstance(m);
        else if (tag.StartsWith(LegacyTag, StringComparison.Ordinal)) ShowLegacy(tag[LegacyTag.Length..]);
    }

    private void OnSearchKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var first = library.Instances.FirstOrDefault(m => Matches(m, (Search.Text ?? "").Trim()));
            if (first is not null) ShowInstance(first);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && !string.IsNullOrEmpty(Search.Text))
        {
            Search.Text = "";
            e.Handled = true;
        }
    }

    // MARK: Pages

    private void ShowDefault()
    {
        var first = Groups(library.Instances).SelectMany(g => g.Items).FirstOrDefault();
        if (first is not null) ShowInstance(first);
        else if (library.LegacyShortcuts.Count > 0) ShowLegacy(library.LegacyShortcuts[0]);
        else ShowEmpty();
    }

    public void ShowInstance(InstanceManifest m)
    {
        route = InstanceTag + m.InstanceSlug;
        PageHost.Content = new InstancePage(this, m);
        SyncSelection();
    }

    private void ShowLegacy(string path)
    {
        route = LegacyTag + path;
        PageHost.Content = MessagePage.Legacy(this, path);
        SyncSelection();
    }

    private void ShowEmpty()
    {
        route = "empty";
        PageHost.Content = MessagePage.Empty(this);
        SyncSelection();
    }

    public void ShowNewInstance(bool website, string? address = null, string? name = null)
    {
        if (route == "new" && PageHost.Content is NewInstancePage page && address is null)
        {
            page.SwitchKind(website);
            return;
        }
        route = "new";
        PageHost.Content = new NewInstancePage(this, website, address, name);
        SyncSelection();
    }

    public void ShowReport(ReportContext? context = null)
    {
        route = "report";
        PageHost.Content = new ReportPage(this, context);
        SyncSelection();
    }

    public void ShowSettings()
    {
        route = "settings";
        if (PageHost.Content is not SettingsPage) PageHost.Content = new SettingsPage(this);
        SyncSelection();
    }

    /// <summary>Leaves the new-instance flow for whatever makes sense.</summary>
    public void CloseNewInstance()
    {
        route = null;
        ShowDefault();
    }

    private void Refresh()
    {
        AppIcons.Forget();
        library.ForgetInstalledApps();
        library.Reload();
        if (SelectedInstance is { } m) ShowInstance(m);
        else if (PageHost.Content is NewInstancePage page) page.ReloadApps();
    }

    // MARK: Actions

    /// <summary>What a report about this instance starts with.</summary>
    public static ReportContext ReportContextFor(InstanceManifest m) =>
        new(m.IsWebsite ? m.WebUrl ?? m.InstanceName : m.SourceName, m.SourceVersionAtCreation, $"{m.Family}, {StatusText.For(m).Title}");

    public async void Open(InstanceManifest m)
    {
        try
        {
            await Task.Run(() => library.Service.OpenOrShow(m));
        }
        catch (Exception error)
        {
            ShowBanner($"Couldn’t open {m.InstanceName}", error.Message, FAInfoBarSeverity.Error);
        }
        library.CheckSoon();
    }

    public async void Quit(InstanceManifest m, bool force = false)
    {
        try
        {
            if (force)
            {
                await Task.Run(() => library.Service.ForceQuit(m));
            }
            else if (await Task.Run(() => library.Service.Quit(m)))
            {
                // Some apps ask to save first, or close to the notification area instead.
                DispatcherTimer.RunOnce(async () =>
                {
                    await library.CheckRunningAsync();
                    if (library.IsRunning(m))
                        ShowBanner($"{m.InstanceName} is still running",
                            $"It may be asking you something, or it closed to the {Words.Tray}.",
                            FAInfoBarSeverity.Warning, ("Force quit", () => Quit(m, force: true)));
                }, TimeSpan.FromSeconds(6));
            }
        }
        catch (Exception error)
        {
            ShowBanner($"Couldn’t quit {m.InstanceName}", error.Message, FAInfoBarSeverity.Error);
        }
        library.CheckSoon();
    }

    public async Task ConfirmMoveToRecycleBin(InstanceManifest m)
    {
        if (dialogOpen) return;
        await library.CheckRunningAsync();
        if (library.IsRunning(m))
        {
            ShowBanner($"Quit {m.InstanceName} first", $"An instance can be moved to {Words.TheBin} only while it is closed.",
                FAInfoBarSeverity.Warning, ("Quit", () => Quit(m)));
            return;
        }
        var size = await Task.Run(() => library.Service.SizeOnDisk(m));
        var unchanged = m.IsWebsite ? "Your browser is not changed." : $"{m.SourceName} itself is not changed.";
        var dialog = new FAContentDialog
        {
            Title = $"Move “{m.InstanceName}” to {Words.TheBin}?",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440,
                Text = $"Its shortcut and its data folder ({Format.Size(size)}) go to {Words.TheBin}, with every account signed in to this instance. {unchanged}",
            },
            PrimaryButtonText = $"Move to {Words.Bin}",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Close,
        };
        if (await ShowDialog(dialog) != FAContentDialogResult.Primary) return;

        var order = Groups(library.Instances).SelectMany(g => g.Items).ToList();
        var index = order.FindIndex(i => i.InstanceSlug == m.InstanceSlug);
        try
        {
            await Task.Run(() => library.Service.MoveToRecycleBin(m));
        }
        catch (Exception error)
        {
            ShowBanner($"Couldn’t move {m.InstanceName} to {Words.TheBin}", error.Message, FAInfoBarSeverity.Error);
            return;
        }
        // Select the neighbour, as File Explorer does.
        var remaining = order.Where(i => i.InstanceSlug != m.InstanceSlug).ToList();
        var next = remaining.Count == 0 ? null : remaining[Math.Clamp(index, 0, remaining.Count - 1)];
        route = next is null ? null : InstanceTag + next.InstanceSlug;
        library.Reload();
        if (next is not null && library.Find(next.InstanceSlug) is { } shown) ShowInstance(shown);
        ShowBanner($"Moved {m.InstanceName} to {Words.TheBin}",
            $"To bring it back, restore its folder and shortcut from {Words.TheBin}.", FAInfoBarSeverity.Success);
    }

    public async Task MoveLegacyToRecycleBin(string path)
    {
        var dialog = new FAContentDialog
        {
            Title = $"Move “{Path.GetFileNameWithoutExtension(path)}” to {Words.TheBin}?",
            Content = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 440, Text = $"Only this shortcut goes to {Words.TheBin}. The app it opens is not changed." },
            PrimaryButtonText = $"Move to {Words.Bin}",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Close,
        };
        if (await ShowDialog(dialog) != FAContentDialogResult.Primary) return;
        try
        {
            await Task.Run(() => library.Service.MoveLegacyShortcutToRecycleBin(path));
        }
        catch (Exception error)
        {
            ShowBanner($"Couldn’t move the shortcut to {Words.TheBin}", error.Message, FAInfoBarSeverity.Error);
            return;
        }
        route = null;
        library.Reload();
    }

    public async void RestoreShortcuts(InstanceManifest m)
    {
        try
        {
            var count = await Task.Run(() => library.Service.RestoreShortcuts(m));
            library.Reload();
            ShowBanner(count == 1 ? "Shortcut restored" : $"{count} shortcuts restored", "", FAInfoBarSeverity.Success);
        }
        catch (Exception error)
        {
            ShowBanner("Couldn’t restore the shortcut", error.Message, FAInfoBarSeverity.Error);
        }
    }

    public void InstanceCreated(InstanceManifest m, bool open)
    {
        route = InstanceTag + m.InstanceSlug;
        library.Reload();
        ShowInstance(library.Find(m.InstanceSlug) ?? m);
        var where = Words.WhereShortcutIs(m.DesktopShortcutPath is not null);
        ShowBanner($"Created {m.InstanceName}", $"You can also open it from its shortcut {where}.", FAInfoBarSeverity.Success);
        if (open) Open(m);
    }

    public async Task<FAContentDialogResult> ShowDialog(FAContentDialog dialog)
    {
        dialogOpen = true;
        try
        {
            return await dialog.ShowAsync(this);
        }
        finally
        {
            dialogOpen = false;
        }
    }

    public void ShowBanner(string title, string message, FAInfoBarSeverity severity, (string Label, Action Run)? action = null)
    {
        bannerTimer?.Stop();
        Banner.Title = title;
        Banner.Message = message;
        Banner.Severity = severity;
        if (action is { } a)
        {
            var button = new Button { Content = a.Label };
            button.Click += (_, _) =>
            {
                Banner.IsOpen = false;
                a.Run();
            };
            Banner.ActionButton = button;
        }
        else
        {
            Banner.ActionButton = null;
        }
        Banner.IsOpen = true;
        if (severity is FAInfoBarSeverity.Success or FAInfoBarSeverity.Informational)
        {
            bannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            bannerTimer.Tick += (_, _) =>
            {
                bannerTimer?.Stop();
                Banner.IsOpen = false;
            };
            bannerTimer.Start();
        }
    }

    // MARK: Keyboard

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (dialogOpen) return;
        var focused = FocusManager?.GetFocusedElement() as Visual;
        var typing = focused is TextBox;
        if (PageHost.Content is IKeyAware page && page.HandleKey(e, typing))
        {
            e.Handled = true;
            return;
        }
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var selected = SelectedInstance;
        switch (e.Key)
        {
            case Key.N when ctrl && !alt:
                ShowNewInstance(website: shift);
                break;
            case Key.F when ctrl && !shift && !alt:
                Search.Focus();
                break;
            case Key.OemComma when ctrl:
                ShowSettings();
                break;
            case Key.F5:
                Refresh();
                break;
            case Key.F1:
                ShowReport(selected is null ? null : ReportContextFor(selected));
                break;
            case Key.Q when ctrl && shift && selected is not null:
                Quit(selected);
                break;
            case Key.E when ctrl && !shift && selected is not null:
                Explorer.Show(library.Service.DataRootOf(selected));
                break;
            case Key.O when ctrl && !shift && selected is not null:
            case Key.Enter when ctrl && selected is not null:
                Open(selected);
                break;
            case Key.Delete when !typing && selected is not null:
                _ = ConfirmMoveToRecycleBin(selected);
                break;
            case Key.Delete when !typing && route?.StartsWith(LegacyTag, StringComparison.Ordinal) == true:
                _ = MoveLegacyToRecycleBin(route[LegacyTag.Length..]);
                break;
            case Key.Enter when !typing && focused?.FindAncestorOfType<FANavigationViewItem>(includeSelf: true) is { Tag: string tag }
                                 && tag.StartsWith(InstanceTag, StringComparison.Ordinal) && library.Find(tag[InstanceTag.Length..]) is { } target:
                ShowInstance(target);
                Open(target);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // MARK: Window

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (library.Settings.KeepInNotificationArea && !ExitRequested)
        {
            e.Cancel = true;
            Hide();
            timer.Stop();
            return;
        }
        timer.Stop();
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        timer.Start();
        _ = library.CheckRunningAsync();
    }
}

using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using OpenParallel.App.Services;
using OpenParallel.Core;
using OpenParallel.Core.Windows;

namespace OpenParallel.App.Views;

/// <summary>The new-instance flow: choose an app (or a website), then decide what the instance gets.</summary>
public partial class NewInstancePage : UserControl, IKeyAware
{
    private enum Step { Picker, AppForm, Website }

    private static readonly (string Name, string Address)[] WebPresets =
    [
        ("WhatsApp Web", "https://web.whatsapp.com"),
        ("Telegram Web", "https://web.telegram.org"),
        ("Gmail", "https://mail.google.com"),
        ("Outlook", "https://outlook.live.com"),
        ("Slack", "https://app.slack.com"),
        ("Discord", "https://discord.com/app"),
        ("Instagram", "https://www.instagram.com"),
        ("X", "https://x.com"),
        ("Messenger", "https://www.messenger.com"),
    ];

    private readonly MainWindow owner;
    private readonly Library library = Library.Shared;
    private Step step;
    /// <summary>The app side remembers whether it was on the list or the form.</summary>
    private Step appStep = Step.Picker;
    private IReadOnlyList<AppChoice>? apps;
    private AppChoice? chosen;
    private AppChoice? formApp;
    private bool appNameEdited;
    private bool webNameEdited;
    /// <summary>The names OpenParallel last filled in; anything else in the box was typed.
    /// (Avalonia raises TextChanged after the setter returns, so a guard flag can't tell.)</summary>
    private string suggestedAppName = "";
    private string suggestedWebName = "";
    /// <summary>A name chosen with a preset or a web alternative, kept while its address stays.</summary>
    private (string Address, string Name)? presetChoice;
    private bool switchingTabs;
    private bool creating;

    public NewInstancePage(MainWindow owner, bool website, string? address = null, string? name = null)
    {
        this.owner = owner;
        InitializeComponent();

        var settings = library.Settings;
        AppOpenToggle.IsChecked = WebOpenToggle.IsChecked = settings.OpenAfterCreating;
        AppDesktopToggle.IsChecked = WebDesktopToggle.IsChecked = settings.DesktopShortcut;
        ArgsBox.FontFamily = App.Monospace;
        WebDetail.Text = StatusText.Website.Detail;
        AppNameCard.IconSource = WebNameCard.IconSource = App.Symbol(FASymbol.Rename);
        SeparateCard.IconSource = App.Symbol(FASymbol.People);
        AppOpenCard.IconSource = WebOpenCard.IconSource = App.Symbol(FASymbol.Play);
        AppDesktopCard.IconSource = WebDesktopCard.IconSource = App.Symbol(FASymbol.Home);
        AppDesktopCard.Description = WebDesktopCard.Description = Words.MenuShortcutAlways;
        if (!OperatingSystem.IsWindows())
        {
            ArgsBox.PlaceholderText = "--disable-gpu";
            FooterHint.Text = "You can also drop a program, AppImage or menu entry here.";
            ToolTip.SetTip(BrowseButton, "Choose a program, AppImage or .desktop file (Ctrl+O)");
        }
        AdvancedExpander.IconSource = App.Symbol(FASymbol.Code);
        AddressCard.IconSource = App.Symbol(FASymbol.Globe);
        BrowserCard.IconSource = App.Symbol(FASymbol.NewWindow);

        KindTabs.SelectedIndex = website ? 1 : 0;
        KindTabs.SelectionChanged += (_, _) =>
        {
            if (!switchingTabs) SwitchKind(KindTabs.SelectedIndex == 1);
        };
        AppSearch.TextChanged += (_, _) => FillList();
        AppList.SelectionChanged += (_, _) =>
        {
            chosen = (AppList.SelectedItem as ListBoxItem)?.Tag as AppChoice;
            if (AppList.SelectedItem is ListBoxItem item) item.BringIntoView();
            UpdateButtons();
        };
        AppList.DoubleTapped += (_, _) => Continue();
        AppNameBox.TextChanged += (_, _) =>
        {
            appNameEdited = (AppNameBox.Text ?? "") != suggestedAppName;
            DescribeName(AppNameBox, AppNameHint);
            UpdateButtons();
        };
        WebNameBox.TextChanged += (_, _) =>
        {
            webNameEdited = (WebNameBox.Text ?? "") != suggestedWebName;
            DescribeName(WebNameBox, WebNameHint);
            UpdateButtons();
        };
        AddressBox.TextChanged += (_, _) =>
        {
            SuggestWebName();
            UpdateButtons();
        };
        BrowserBox.SelectionChanged += (_, _) => UpdateButtons();
        AdvancedExpander.PropertyChanged += (_, e) =>
        {
            if (e.Property == FASettingsExpander.IsExpandedProperty && AdvancedExpander.IsExpanded)
                DispatcherTimer.RunOnce(() => (EnvItem.IsVisible ? (Control)EnvItem : ArgsItem).BringIntoView(), TimeSpan.FromMilliseconds(250));
        };
        AddEnvButton.Click += (_, _) => AddEnvRow("", "", focus: true);
        BrowseButton.Click += async (_, _) => await BrowseAsync();
        SecondaryButton.Click += (_, _) => Back();
        PrimaryButton.Click += (_, _) => Primary();
        foreach (var (presetName, presetAddress) in WebPresets)
        {
            var button = new Button { Content = presetName, Margin = new(0, 0, 8, 8) };
            ToolTip.SetTip(button, presetAddress);
            button.Click += (_, _) => UsePreset(presetName, presetAddress);
            Presets.Children.Add(button);
        }
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        FillBrowsers();
        if (address is not null) UsePreset(name ?? "", address);
        Go(website ? Step.Website : Step.Picker);
        _ = LoadAppsAsync(refresh: false);
    }

    // MARK: Steps

    public void SwitchKind(bool website) => Go(website ? Step.Website : appStep);

    private void Go(Step next)
    {
        step = next;
        if (next != Step.Website) appStep = next;
        PickerPanel.IsVisible = next == Step.Picker;
        AppForm.IsVisible = next == Step.AppForm;
        WebForm.IsVisible = next == Step.Website;
        BrowseButton.IsVisible = FooterHint.IsVisible = next == Step.Picker;
        SecondaryButton.Content = next == Step.AppForm ? "Back" : "Cancel";
        ToolTip.SetTip(SecondaryButton, next == Step.AppForm ? "Back to the list (Alt+Left)" : "Cancel (Esc)");
        PrimaryButton.Content = next == Step.Picker ? "Continue" : "Create";
        ToolTip.SetTip(PrimaryButton, next == Step.Picker ? "Continue (Enter)" : "Create (Ctrl+Enter)");
        switchingTabs = true;
        KindTabs.SelectedIndex = next == Step.Website ? 1 : 0;
        switchingTabs = false;
        ErrorBar.IsOpen = false;
        UpdateButtons();
        Dispatcher.UIThread.Post(FocusFirst, DispatcherPriority.Loaded);
    }

    private void FocusFirst()
    {
        switch (step)
        {
            case Step.Picker:
                AppSearch.Focus();
                break;
            case Step.AppForm when AppOptions.IsVisible:
                AppNameBox.Focus();
                AppNameBox.SelectAll();
                break;
            case Step.Website:
                AddressBox.Focus();
                break;
        }
    }

    private void Back()
    {
        if (step == Step.AppForm) Go(Step.Picker);
        else owner.CloseNewInstance();
    }

    private void Primary()
    {
        switch (step)
        {
            case Step.Picker:
                Continue();
                break;
            case Step.AppForm:
                _ = CreateAppAsync();
                break;
            case Step.Website:
                _ = CreateWebsiteAsync();
                break;
        }
    }

    private void UpdateButtons()
    {
        PrimaryButton.IsEnabled = !creating && step switch
        {
            Step.Picker => chosen is not null,
            Step.AppForm => formApp is { App.CanCreate: true } && !string.IsNullOrWhiteSpace(AppNameBox.Text),
            _ => WebAddress() is not null && SelectedBrowser() is not null && !string.IsNullOrWhiteSpace(WebNameBox.Text),
        };
    }

    // MARK: App list

    public void ReloadApps() => _ = LoadAppsAsync(refresh: true);

    private async Task LoadAppsAsync(bool refresh)
    {
        LoadingPanel.IsVisible = true;
        NoAppsText.IsVisible = false;
        try
        {
            apps = await library.InstalledAppsAsync(refresh);
        }
        catch (Exception error)
        {
            apps = [];
            ShowError("Couldn’t list the installed apps", error.Message);
        }
        LoadingPanel.IsVisible = false;
        FillList();
        FillBrowsers();
    }

    private void FillList()
    {
        if (apps is null) return;
        var query = (AppSearch.Text ?? "").Trim();
        var keep = chosen;
        AppList.Items.Clear();
        ListBoxItem? first = null;
        ListBoxItem? again = null;
        foreach (var group in Enum.GetValues<Support>())
        {
            var rows = apps
                .Where(a => a.Support == group && (query.Length == 0 || a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (rows.Count == 0) continue;
            var header = new TextBlock { Text = SupportText.Title(group), FontWeight = FontWeight.SemiBold, Margin = new(0, 10, 0, 2) };
            App.Dynamic(header, TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            AppList.Items.Add(new ListBoxItem { Content = header, IsEnabled = false, Focusable = false });
            foreach (var app in rows)
            {
                var item = new ListBoxItem { Content = Row(app), Tag = app };
                AutomationProperties.SetName(item, app.Name);
                AppList.Items.Add(item);
                first ??= item;
                if (keep is not null && string.Equals(keep.App.ExePath, app.App.ExePath, StringComparison.OrdinalIgnoreCase)) again = item;
            }
        }
        // Like the Start menu: the best match is ready for Enter.
        AppList.SelectedItem = again ?? first;
        NoAppsText.IsVisible = first is null;
        NoAppsText.Text = apps.Count == 0
            ? "No installed apps were found. Choose a program with Browse."
            : $"No apps match “{query}”. Choose a program with Browse.";
    }

    private static Control Row(AppChoice choice)
    {
        var app = choice.App;
        var detail = string.Join("  ·  ", new[] { app.Version, app.Company }.Where(s => s.Length > 0));
        Control icon = AppIcons.For(app) is { } bitmap
            ? new Image { Source = bitmap, Width = 28, Height = 28 }
            : new FASymbolIcon { Symbol = FASymbol.Document, FontSize = 22 };
        var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new(2, 3) };
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);
        var text = new StackPanel { Margin = new(12, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = choice.Name, TextTrimming = TextTrimming.CharacterEllipsis });
        if (detail.Length > 0) text.Children.Add(new TextBlock { Text = detail, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var status = new TextBlock { Text = StatusText.For(app).Title, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(status, 2);
        grid.Children.Add(status);
        return grid;
    }

    private void MoveSelection(int delta)
    {
        var items = AppList.Items.OfType<ListBoxItem>().Where(i => i.Tag is AppChoice).ToList();
        if (items.Count == 0) return;
        var index = items.IndexOf((AppList.SelectedItem as ListBoxItem)!);
        var next = index < 0 ? 0 : Math.Clamp(index + delta, 0, items.Count - 1);
        AppList.SelectedItem = items[next];
    }

    private void Continue()
    {
        if (chosen is null) return;
        if (formApp is null || !string.Equals(formApp.App.ExePath, chosen.App.ExePath, StringComparison.OrdinalIgnoreCase))
            appNameEdited = false;
        ShowAppForm(chosen);
    }

    private void ShowAppForm(AppChoice choice)
    {
        formApp = choice;
        var app = choice.App;
        var status = StatusText.For(app);
        AppIconHost.Content = AppIcons.For(app) is { } bitmap
            ? new Image { Source = bitmap, Width = 40, Height = 40 }
            : new FASymbolIcon { Symbol = FASymbol.Document, FontSize = 32 };
        AppNameText.Text = choice.Name;
        var detail = string.Join("  ·  ", new[] { app.Version.Length > 0 ? $"Version {app.Version}" : "", app.Company }.Where(s => s.Length > 0));
        AppVersionText.Text = detail.Length > 0 ? detail : Explorer.Friendly(app.ExePath);
        ToolTip.SetTip(AppVersionText, app.ExePath);
        StatusIcon.Symbol = status.Symbol;
        StatusTitle.Text = status.Title;
        StatusDetail.Text = status.Detail;

        if (!app.CanCreate)
        {
            AppOptions.IsVisible = false;
            UnsupportedBar.Severity = app.WebAlternative is null ? FAInfoBarSeverity.Error : FAInfoBarSeverity.Informational;
            UnsupportedBar.Title = app.WebAlternative is null
                ? "OpenParallel can’t make a separate copy of this app."
                : $"Use {choice.Name} in its own window instead";
            UnsupportedBar.Message = app.WebAlternative is null
                ? "Windows or the app itself doesn’t allow a copy with its own data."
                : $"A website instance of {new Uri(app.WebAlternative).Host} opens like an app, with a login of its own.";
            if (app.WebAlternative is { } alternative)
            {
                var button = new Button { Content = "Make a website instance instead" };
                button.Click += (_, _) => UsePreset($"{choice.Name} Web", alternative, switchToWebsite: true);
                UnsupportedBar.ActionButton = button;
            }
            else
            {
                UnsupportedBar.ActionButton = null;
            }
            UnsupportedBar.IsOpen = true;
        }
        else
        {
            UnsupportedBar.IsOpen = false;
            AppOptions.IsVisible = true;
            if (!appNameEdited)
            {
                suggestedAppName = library.Service.SuggestName(choice.Name);
                AppNameBox.Text = suggestedAppName;
            }
            SeparateToggle.IsChecked = app.SeparatesData;
            SeparateToggle.IsEnabled = app.SeparatesData;
            SeparateCard.Description = app.SeparatesData
                ? "Sign in to a different account here than in the original."
                : $"{choice.Name} keeps one set of data for every copy, so this instance shares it.";
            ArgsItem.Description = $"Added each time {choice.Name} starts";
        }
        DescribeName(AppNameBox, AppNameHint);
        Go(Step.AppForm);
    }

    // MARK: Choosing a program by hand

    private async Task BrowseAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a program",
            AllowMultiple = false,
            FileTypeFilter = OperatingSystem.IsWindows()
                ? [new FilePickerFileType("Programs and shortcuts") { Patterns = ["*.exe", "*.lnk"] }]
                : [new FilePickerFileType("Programs, AppImages and menu entries") { Patterns = ["*"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await PickAsync(path);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = step != Step.Website && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (step == Step.Website) return;
        if (e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath() is { } path) await PickAsync(path);
    }

    private async Task PickAsync(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var exe = library.Service.Platform.Apps.ResolvePicked(library.Service.Platform, path);
        if (exe is null)
        {
            ShowError("That isn’t a program OpenParallel can copy.", OperatingSystem.IsWindows()
                ? "Choose an .exe file, or a shortcut to one."
                : "Choose a program, an AppImage, or an app’s .desktop entry.");
            return;
        }
        try
        {
            var inspected = await Task.Run(() => library.Service.Inspect(exe));
            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) name = inspected.Name;
            chosen = new AppChoice(name, inspected);
            appNameEdited = false;
            ShowAppForm(chosen);
        }
        catch (Exception error)
        {
            ShowError("Couldn’t read that program", error.Message);
        }
    }

    // MARK: Website

    private void FillBrowsers()
    {
        var keep = SelectedBrowser();
        var found = library.Service.Platform.Apps.Browsers(apps?.Select(a => a.App)).ToList();
        BrowserBox.Items.Clear();
        foreach (var exe in found)
        {
            var listed = apps?.FirstOrDefault(a => string.Equals(a.App.ExePath, exe, StringComparison.OrdinalIgnoreCase))?.Name;
            var item = new ComboBoxItem { Content = listed ?? Format.ProgramName(exe), Tag = exe };
            ToolTip.SetTip(item, exe);
            BrowserBox.Items.Add(item);
        }
        BrowserBox.SelectedItem = BrowserBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, keep))
                                  ?? BrowserBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        NoBrowserBar.IsOpen = found.Count == 0;
        UpdateButtons();
    }

    private string? SelectedBrowser() => (BrowserBox.SelectedItem as ComboBoxItem)?.Tag as string;

    /// <summary>The typed address as an http(s) URL, adding https:// when left out.</summary>
    private string? WebAddress()
    {
        var text = (AddressBox.Text ?? "").Trim();
        if (text.Length == 0) return null;
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
               && (uri.Host.Contains('.') || uri.Host == "localhost")
            ? text
            : null;
    }

    private void SuggestWebName()
    {
        if (webNameEdited) return;
        var address = WebAddress();
        if (address is null) return;
        var host = new Uri(address).Host;
        string name;
        if (presetChoice is { } chosenPreset && string.Equals(chosenPreset.Address, address, StringComparison.OrdinalIgnoreCase))
            name = chosenPreset.Name;
        else
            name = WebPresets.FirstOrDefault(p => new Uri(p.Address).Host.Equals(host, StringComparison.OrdinalIgnoreCase)).Name
                   ?? (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host);
        suggestedWebName = name;
        WebNameBox.Text = name;
    }

    private void UsePreset(string name, string address, bool switchToWebsite = false)
    {
        presetChoice = name.Length > 0 ? (address, name) : null;
        webNameEdited = false;
        AddressBox.Text = address;
        SuggestWebName();
        if (switchToWebsite) Go(Step.Website);
        UpdateButtons();
    }

    // MARK: Creating

    private async Task CreateAppAsync()
    {
        if (formApp is null || creating || !PrimaryButton.IsEnabled) return;
        var environment = new Dictionary<string, string>();
        foreach (var row in EnvRows.Children.OfType<Grid>())
        {
            var key = ((row.Children[0] as TextBox)?.Text ?? "").Trim();
            var value = (row.Children[2] as TextBox)?.Text ?? "";
            if (key.Length == 0 && value.Length == 0) continue;
            if (!Regex.IsMatch(key, "^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                AdvancedExpander.IsExpanded = true;
                ShowError("Check the environment variables", $"“{key}” isn’t a valid name. Use letters, digits and _ and start with a letter.");
                return;
            }
            environment[key] = value;
        }
        var request = new InstanceRequest
        {
            Name = (AppNameBox.Text ?? "").Trim(),
            AppPath = formApp.App.ExePath,
            SeparateData = SeparateToggle.IsChecked == true,
            ExtraArguments = library.Service.Platform.SplitArguments(ArgsBox.Text ?? ""),
            Environment = environment,
            DesktopShortcut = AppDesktopToggle.IsChecked == true,
        };
        await CreateAsync(request, formApp.App with { Name = formApp.Name }, AppOpenToggle.IsChecked == true);
    }

    private async Task CreateWebsiteAsync()
    {
        if (creating || !PrimaryButton.IsEnabled) return;
        var request = new InstanceRequest
        {
            Name = (WebNameBox.Text ?? "").Trim(),
            AppPath = SelectedBrowser(),
            WebUrl = WebAddress(),
            DesktopShortcut = WebDesktopToggle.IsChecked == true,
        };
        await CreateAsync(request, null, WebOpenToggle.IsChecked == true);
    }

    private async Task CreateAsync(InstanceRequest request, InspectedApp? app, bool open)
    {
        creating = true;
        ErrorBar.IsOpen = false;
        PrimaryButton.Content = "Creating…";
        UpdateButtons();
        try
        {
            var manifest = await Task.Run(() => library.Service.Create(request, app));
            owner.InstanceCreated(manifest, open);
        }
        catch (Exception error)
        {
            ShowError("Couldn’t create the instance", error.Message);
        }
        finally
        {
            creating = false;
            PrimaryButton.Content = "Create";
            UpdateButtons();
        }
    }

    // MARK: Helpers

    private void AddEnvRow(string key, string value, bool focus)
    {
        var grid = new Grid { ColumnDefinitions = new("*,Auto,2*,Auto") };
        var name = new TextBox { Text = key, PlaceholderText = "NAME", FontFamily = App.Monospace };
        AutomationProperties.SetName(name, "Variable name");
        var equals = new TextBlock { Text = "=", Margin = new(8, 0), VerticalAlignment = VerticalAlignment.Center, Classes = { "secondary" } };
        var text = new TextBox { Text = value, PlaceholderText = "value", FontFamily = App.Monospace };
        AutomationProperties.SetName(text, "Variable value");
        var remove = new Button { Content = new FASymbolIcon { Symbol = FASymbol.Delete, FontSize = 14 }, Margin = new(8, 0, 0, 0) };
        ToolTip.SetTip(remove, "Remove this variable");
        AutomationProperties.SetName(remove, "Remove this variable");
        remove.Click += (_, _) =>
        {
            EnvRows.Children.Remove(grid);
            EnvItem.IsVisible = EnvRows.Children.Count > 0;
        };
        Grid.SetColumn(equals, 1);
        Grid.SetColumn(text, 2);
        Grid.SetColumn(remove, 3);
        grid.Children.Add(name);
        grid.Children.Add(equals);
        grid.Children.Add(text);
        grid.Children.Add(remove);
        EnvRows.Children.Add(grid);
        EnvItem.IsVisible = true;
        AdvancedExpander.IsExpanded = true;
        if (focus) Dispatcher.UIThread.Post(() => name.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>Says what name the instance will really get when the typed one is taken.</summary>
    private void DescribeName(TextBox box, TextBlock hint)
    {
        var name = (box.Text ?? "").Trim();
        string? text = null;
        if (name.Length > 0)
        {
            try
            {
                var reserved = library.Service.ReserveName(name);
                if (reserved != name) text = $"“{name}” is taken, so this one will be called “{reserved}”.";
            }
            catch (Exception)
            {
            }
        }
        hint.Text = text;
        hint.IsVisible = text is not null;
    }

    private void ShowError(string title, string message)
    {
        ErrorBar.Title = title;
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    // MARK: Keyboard

    public bool HandleKey(KeyEventArgs e, bool typing)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        switch (e.Key)
        {
            case Key.D1 or Key.NumPad1 when ctrl:
                SwitchKind(website: false);
                return true;
            case Key.D2 or Key.NumPad2 when ctrl:
                SwitchKind(website: true);
                return true;
            case Key.O when ctrl && step == Step.Picker:
                _ = BrowseAsync();
                return true;
            case Key.Escape when focused == AppSearch && !string.IsNullOrEmpty(AppSearch.Text):
                AppSearch.Text = "";
                return true;
            case Key.Escape:
            case Key.Left when alt:
                Back();
                return true;
            case Key.Down when step == Step.Picker && focused == AppSearch:
                MoveSelection(1);
                return true;
            case Key.Up when step == Step.Picker && focused == AppSearch:
                MoveSelection(-1);
                return true;
            case Key.Enter when ctrl && step != Step.Picker:
                Primary();
                return true;
            case Key.Enter when step == Step.Picker && (focused == AppSearch || focused is ListBoxItem):
                Continue();
                return true;
            case Key.Enter when step != Step.Picker && focused is TextBox box && !EnvRows.Children.OfType<Grid>().Any(g => g.Children.Contains(box)):
                Primary();
                return true;
            default:
                return false;
        }
    }
}

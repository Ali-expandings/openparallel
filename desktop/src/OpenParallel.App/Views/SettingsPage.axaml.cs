using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using FluentAvalonia.UI.Controls;
using OpenParallel.App.Services;

namespace OpenParallel.App.Views;

public partial class SettingsPage : UserControl
{
    private static readonly (string Keys, string Action)[] Shortcuts =
    [
        ("Ctrl+N", "New instance"),
        ("Ctrl+Shift+N", "New website instance"),
        ("Ctrl+F", "Search instances"),
        ("Enter, Ctrl+O, double-click", "Open the instance, or bring it forward"),
        ("Ctrl+Shift+Q", "Quit the instance"),
        ("Delete", $"Move the instance to {Words.TheBin} (asks first)"),
        ("Ctrl+E", "Open its data folder"),
        ("F5", "Refresh"),
        ("Ctrl+,", "Settings"),
        ("F1", "Report a problem (about the selected instance, if any)"),
        ("Ctrl+1, Ctrl+2", "App or Website, in New instance"),
        ("Up, Down, Enter", "Choose an app from the search box"),
        ("Ctrl+Enter", "Create"),
        ("Esc, Alt+Left", "Back, or cancel New instance"),
    ];

    private readonly Library library = Library.Shared;
    private bool loading = true;

    public SettingsPage(MainWindow owner)
    {
        InitializeComponent();
        var settings = library.Settings;

        ThemeCard.IconSource = App.Symbol(FASymbol.DarkTheme);
        DesktopCard.Description = OperatingSystem.IsWindows()
            ? "New instances always get a Start menu shortcut, in the OpenParallel folder."
            : "New instances always appear in the applications menu.";
        TrayCard.Header = $"Keep OpenParallel in the {Words.Tray}";
        AutomationProperties.SetName(TrayToggle, TrayCard.Header as string);
        TrayCard.Description = OperatingSystem.IsWindows()
            ? "Closing the window leaves OpenParallel’s icon next to the clock, with your instances in its menu."
            : "Closing the window leaves OpenParallel’s icon in the panel, with your instances in its menu (GNOME needs the AppIndicator extension).";
        ShortcutsCard.Header = OperatingSystem.IsWindows() ? "Start menu shortcuts" : "Menu entries";
        OpenCard.IconSource = App.Symbol(FASymbol.NewWindow);
        DesktopCard.IconSource = App.Symbol(FASymbol.Home);
        TrayCard.IconSource = App.Symbol(FASymbol.Pin);
        ShortcutsCard.IconSource = App.Symbol(FASymbol.Link);
        DataCard.IconSource = App.Symbol(FASymbol.Folder);
        LogsCard.IconSource = App.Symbol(FASymbol.Document);
        KeysCard.IconSource = App.Symbol(FASymbol.Keyboard);
        ReportCard.IconSource = App.Symbol(FASymbol.Help);
        ReportButton.Click += (_, _) => owner.ShowReport();
        using (var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://OpenParallel/Assets/OpenParallel.png")))
            AboutCard.IconSource = new FAImageIconSource { Source = new Avalonia.Media.Imaging.Bitmap(stream) };

        ThemeBox.SelectedItem = ThemeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, settings.Theme))
                                ?? ThemeBox.Items[0];
        OpenToggle.IsChecked = settings.OpenAfterCreating;
        DesktopToggle.IsChecked = settings.DesktopShortcut;
        TrayToggle.IsChecked = settings.KeepInNotificationArea;

        ThemeBox.SelectionChanged += (_, _) =>
        {
            if (loading || (ThemeBox.SelectedItem as ComboBoxItem)?.Tag is not string theme) return;
            settings.Theme = theme;
            settings.Save();
            App.ApplyTheme(theme);
        };
        OpenToggle.IsCheckedChanged += (_, _) => Save(() => settings.OpenAfterCreating = OpenToggle.IsChecked == true);
        DesktopToggle.IsCheckedChanged += (_, _) => Save(() => settings.DesktopShortcut = DesktopToggle.IsChecked == true);
        TrayToggle.IsCheckedChanged += (_, _) =>
        {
            Save(() => settings.KeepInNotificationArea = TrayToggle.IsChecked == true);
            (Application.Current as App)?.UpdateTray();
        };

        var service = library.Service;
        ShortcutsCard.Description = Explorer.Friendly(service.ShortcutsFolder);
        DataCard.Description = Explorer.Friendly(service.InstancesRoot);
        LogsCard.Description = Explorer.Friendly(service.LogsFolder);
        ShortcutsButton.Click += (_, _) => OpenFolder(service.ShortcutsFolder);
        DataButton.Click += (_, _) => OpenFolder(service.InstancesRoot);
        LogsButton.Click += (_, _) => OpenFolder(service.LogsFolder);

        foreach (var (keys, action) in Shortcuts)
        {
            KeysCard.Items.Add(new FASettingsExpanderItem
            {
                Content = action,
                Footer = new TextBlock { Text = keys, FontFamily = App.Monospace, Classes = { "secondary" }, VerticalAlignment = VerticalAlignment.Center },
            });
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "Development build" : $"Version {version.Major}.{version.Minor}.{version.Build}";
        loading = false;
    }

    private void Save(Action change)
    {
        if (loading) return;
        change();
        library.Settings.Save();
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception)
        {
        }
        Explorer.Show(path);
    }
}

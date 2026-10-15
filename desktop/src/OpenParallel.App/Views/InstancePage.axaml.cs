using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using OpenParallel.App.Services;
using OpenParallel.Core;

namespace OpenParallel.App.Views;

/// <summary>One instance: what it is, where its data lives, and what can be done with it.</summary>
public partial class InstancePage : UserControl
{
    private readonly MainWindow owner;
    private readonly InstanceManifest m;
    private readonly Library library = Library.Shared;

    public InstancePage(MainWindow owner, InstanceManifest manifest)
    {
        this.owner = owner;
        m = manifest;
        InitializeComponent();
        Populate();
        UpdateRunning();
        _ = MeasureAsync();
    }

    public InstanceManifest Manifest => m;

    private string DataRoot => library.Service.DataRootOf(m);

    private void Populate()
    {
        var status = StatusText.For(m);
        RemoveCard.Header = $"Move to {Words.Bin}";
        RemoveButton.Content = $"Move to {Words.Bin}…";
        ShortcutCard.Header = OperatingSystem.IsWindows() ? "Start menu" : "Applications menu";
        foreach (var button in new[] { SourceButton, ShortcutButton, DesktopButton }) ToolTip.SetTip(button, $"Show it in {Words.FileManager}");
        IconHost.Content = AppIcons.For(m) is { } bitmap
            ? new Image { Source = bitmap, Width = 48, Height = 48 }
            : new FASymbolIcon { Symbol = m.IsWebsite ? FASymbol.Globe : FASymbol.Document, FontSize = 40 };
        NameText.Text = m.InstanceName;

        CompatibilityCard.IconSource = App.Symbol(status.Symbol);
        CompatibilityCard.Description = status.Detail;
        CompatibilityCard.Footer = App.Secondary(status.Title);

        var own = m.IsWebsite || m.ProfileIsolation;
        OwnDataCard.IconSource = App.Symbol(FASymbol.People);
        OwnDataCard.Description = m.IsWebsite
            ? "Cookies, sign-ins and storage stay in this instance’s data folder."
            : own
                ? "Accounts you sign in to here stay in this instance’s data folder."
                : $"{m.SourceName} keeps one set of data for every copy.";
        OwnDataCard.Footer = App.Secondary(own ? "Yes" : $"No, shared with {m.SourceName}");

        FolderCard.IconSource = App.Symbol(FASymbol.Folder);
        FolderCard.Description = Explorer.Friendly(DataRoot);
        FolderButton.Click += (_, _) => Explorer.Show(DataRoot);
        SizeCard.IconSource = App.Symbol(FASymbol.Library);

        if (m.IsWebsite)
        {
            SourceCard.Header = "Address";
            SourceCard.IconSource = App.Symbol(FASymbol.Globe);
            var browserName = m.OpenedWith ?? (File.Exists(m.LaunchTarget) ? Format.ProgramName(m.LaunchTarget) : null);
            SourceCard.Description = browserName is null ? m.WebUrl : $"{m.WebUrl}\nOpens in {browserName}";
            SourceButton.Content = "Copy address";
            SourceButton.Click += async (_, _) => await CopyAsync(m.WebUrl ?? "");
            if (!File.Exists(m.LaunchTarget))
                ShowSourceBar("The browser this instance opens in is no longer installed.",
                    "Create the website instance again with a browser that is installed.");
        }
        else
        {
            SourceCard.Header = m.SourceName;
            SourceCard.IconSource = AppIcons.IconSource(m);
            // A Store app starts by its id, not a file; its folder changes with every update.
            var packaged = m.LaunchMode == "packaged" && OperatingSystem.IsWindows();
            var current = packaged ? OpenParallel.Core.Windows.PackagedApps.CurrentExe(m.LaunchTarget) : null;
            var installed = packaged ? current is not null : File.Exists(m.LaunchTarget);
            var location = current ?? (File.Exists(m.SourcePath) ? m.SourcePath : m.LaunchTarget);
            SourceCard.Description = (m.SourceVersionAtCreation.Length > 0 ? $"Version {m.SourceVersionAtCreation} when this instance was made\n" : "")
                                     + Explorer.Friendly(location);
            SourceButton.Click += (_, _) => Explorer.Show(location);
            if (!installed)
            {
                SourceButton.IsEnabled = false;
                if (packaged)
                    ShowSourceBar($"{m.SourceName} is no longer installed.",
                        $"Install {m.SourceName} from the Microsoft Store again and this instance opens as before. Its data folder is kept.");
                else
                    ShowSourceBar($"{m.SourceName} is no longer at this location.",
                        "If it was moved or reinstalled somewhere else, create a new instance of it. This instance’s data folder is kept.");
            }
        }

        ShortcutCard.IconSource = App.Symbol(FASymbol.Link);
        ShortcutCard.Description = Explorer.Friendly(m.ShortcutPath);
        ShortcutButton.Click += (_, _) => Explorer.Show(m.ShortcutPath);
        if (m.DesktopShortcutPath is { } desktop)
        {
            DesktopCard.IsVisible = true;
            DesktopCard.IconSource = App.Symbol(FASymbol.Home);
            DesktopCard.Description = Explorer.Friendly(desktop);
            DesktopButton.Click += (_, _) => Explorer.Show(desktop);
        }
        var missing = library.Service.MissingShortcuts(m);
        if (missing.Count > 0)
        {
            ShortcutButton.IsEnabled = File.Exists(m.ShortcutPath);
            DesktopButton.IsEnabled = m.DesktopShortcutPath is not null && File.Exists(m.DesktopShortcutPath);
            ShortcutBar.Title = missing.Count == 1 ? "A shortcut of this instance is missing." : "This instance’s shortcuts are missing.";
            ShortcutBar.Message = "OpenParallel can write it again from what it recorded when the instance was made.";
            var restore = new Button { Content = "Restore" };
            restore.Click += (_, _) => owner.RestoreShortcuts(m);
            ShortcutBar.ActionButton = restore;
            ShortcutBar.IsOpen = true;
        }

        LaunchCard.IconSource = App.Symbol(FASymbol.Code);
        AddLaunchItem("Program", m.LaunchMode == "packaged"
            ? $"{m.SourceName} from the Microsoft Store ({m.LaunchTarget[OpenParallel.Core.Windows.PackagedApps.ActivationPrefix.Length..]})"
            : Explorer.Friendly(m.LaunchTarget));
        var platform = library.Service.Platform;
        AddLaunchItem("Arguments", m.LaunchArguments.Count > 0 ? platform.JoinArguments(m.LaunchArguments) : "None");
        if (m.Environment.Count > 0)
            AddLaunchItem("Environment", string.Join(Environment.NewLine, m.Environment.Select(p => $"{p.Key} = {p.Value}")));
        var how = m.LaunchMode switch
        {
            "squirrel" => $"Started through {m.SourceName}’s Update.exe, so it always opens the newest installed version.",
            "launcher" => "Its shortcut opens it through OpenParallel, which sets the environment above first.",
            "packaged" => "A Microsoft Store app starts through Windows, so its shortcut opens it through OpenParallel, which asks Windows to start it with the arguments above.",
            "web-app" => "Opened as an app window with its own browser profile.",
            "flatpak" => "Runs inside its Flatpak sandbox, which is given this instance’s folder and nothing else of yours.",
            "snap" => "A Snap: its data is kept inside the snap’s own folder, under OpenParallel.",
            "appimage" => "An AppImage: if the file is replaced by a newer version at the same place, the shortcut keeps working.",
            _ => null,
        };
        if (how is not null) AddLaunchItem("Notes", how);
        var log = Path.Combine(library.Service.LogsFolder, $"{m.InstanceSlug}.log");
        if (File.Exists(log))
        {
            var openLog = new Button { Content = "Open log" };
            openLog.Click += (_, _) => Explorer.OpenFile(log);
            LaunchCard.Items.Add(new FASettingsExpanderItem
            {
                Content = "Log",
                Description = "Problems OpenParallel ran into while opening this instance.",
                Footer = openLog,
            });
        }
        CopyButton.Click += async (_, _) =>
        {
            var environment = OperatingSystem.IsWindows() || m.Environment.Count == 0 ? "" : "env " + platform.JoinArguments(m.Environment.Select(e => $"{e.Key}={e.Value}")) + " ";
            var command = environment + platform.JoinArguments([m.LaunchTarget, .. m.LaunchArguments]);
            await CopyAsync(command);
        };

        CreatedCard.IconSource = App.Symbol(FASymbol.Clock);
        CreatedText.Text = Format.Date(m.CreatedAt);

        RemoveCard.IconSource = App.Symbol(FASymbol.Delete);
        RemoveCard.Description = m.IsWebsite
            ? "Removes its shortcuts and its data folder, with its cookies and sign-ins. Your browser is not changed."
            : $"Removes its shortcuts and its data folder, with its sign-ins. {m.SourceName} itself is not changed.";
        RemoveButton.Click += (_, _) => _ = owner.ConfirmMoveToRecycleBin(m);

        ReportCard.IconSource = App.Symbol(FASymbol.Help);
        ReportButton.Click += (_, _) => owner.ShowReport(MainWindow.ReportContextFor(m));
        OpenButton.Click += (_, _) => owner.Open(m);
        QuitButton.Click += (_, _) => owner.Quit(m);
    }

    private void ShowSourceBar(string title, string message)
    {
        SourceBar.Title = title;
        SourceBar.Message = message;
        SourceBar.IsOpen = true;
    }

    private void AddLaunchItem(string title, string value)
    {
        LaunchCard.Items.Add(new FASettingsExpanderItem
        {
            Content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = title },
                    new SelectableTextBlock
                    {
                        Text = value,
                        TextWrapping = TextWrapping.Wrap,
                        FontFamily = App.Monospace,
                        Classes = { "secondary" },
                    },
                },
            },
        });
    }

    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
            owner.ShowBanner("Copied", text, FAInfoBarSeverity.Informational);
        }
    }

    private async Task MeasureAsync()
    {
        var size = await Task.Run(() =>
        {
            try { return library.Service.SizeOnDisk(m); }
            catch (Exception) { return -1L; }
        });
        SizeText.Text = size < 0 ? "Unknown" : Format.Size(size);
    }

    public void UpdateRunning()
    {
        var running = library.IsRunning(m);
        App.Dynamic(RunningDot, Avalonia.Controls.Shapes.Shape.FillProperty, running ? "SystemFillColorSuccessBrush" : "TextFillColorTertiaryBrush");
        StateText.Text = running ? "Running" : "Not running";
        OpenButton.Content = running ? "Show" : "Open";
        ToolTip.SetTip(OpenButton, running ? "Bring its window forward (Ctrl+O)" : "Open (Ctrl+O)");
        QuitButton.IsVisible = running;
        RemoveButton.IsEnabled = !running;
        ToolTip.SetTip(RemoveButton, running ? "Quit it first" : $"Move to {Words.Bin} (Delete)");
    }
}

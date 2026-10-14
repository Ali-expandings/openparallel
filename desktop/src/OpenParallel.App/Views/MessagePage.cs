using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FluentAvalonia.UI.Controls;
using OpenParallel.App.Services;

namespace OpenParallel.App.Views;

/// <summary>The simple pages: no instances yet, and a shortcut OpenParallel no longer manages.</summary>
public static class MessagePage
{
    public static Control Empty(MainWindow owner)
    {
        using var stream = AssetLoader.Open(new Uri("avares://OpenParallel/Assets/OpenParallel.png"));
        var newApp = new Button { Content = "New instance", Classes = { "accent" }, MinWidth = 150 };
        ToolTip.SetTip(newApp, "Ctrl+N");
        newApp.Click += (_, _) => owner.ShowNewInstance(website: false);
        var newWeb = new Button { Content = "New website instance", MinWidth = 150 };
        ToolTip.SetTip(newWeb, "Ctrl+Shift+N");
        newWeb.Click += (_, _) => owner.ShowNewInstance(website: true);
        return new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 10,
            MaxWidth = 460,
            Margin = new(36),
            Children =
            {
                new Image { Source = new Bitmap(stream), Width = 96, Height = 96, Margin = new(0, 0, 0, 8) },
                new TextBlock { Text = "No instances yet", FontSize = 20, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock
                {
                    Text = "An instance is a second copy of an app with its own login and data. The original app is never changed.",
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Classes = { "secondary" },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 8,
                    Margin = new(0, 12, 0, 0),
                    Children = { newApp, newWeb },
                },
            },
        };
    }

    public static Control Legacy(MainWindow owner, string path)
    {
        var library = Library.Shared;
        var shortcut = library.Service.Platform.ReadShortcut(path);
        var page = new StackPanel { Classes = { "page" } };
        page.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(path), Classes = { "title" }, Margin = new(0, 4, 0, 2) });
        page.Children.Add(new TextBlock { Text = "A shortcut OpenParallel doesn’t manage", Classes = { "secondary" }, Margin = new(0, 0, 0, 12) });
        page.Children.Add(new FAInfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = FAInfoBarSeverity.Informational,
            Title = OperatingSystem.IsWindows()
                ? "This shortcut is in OpenParallel’s Start menu folder, but no instance owns it."
                : "This menu entry was made by OpenParallel, but no instance owns it.",
            Message = $"It may be left over from an instance whose data folder was removed. To keep using the app this way, create a new instance, then move this shortcut to {Words.TheBin}.",
        });

        var show = new Button { Content = "Open location" };
        ToolTip.SetTip(show, $"Show it in {Words.FileManager}");
        show.Click += (_, _) => Explorer.Show(path);
        page.Children.Add(new TextBlock { Text = "Shortcut", Classes = { "section" } });
        page.Children.Add(new FASettingsExpander
        {
            Header = "Location",
            Description = Explorer.Friendly(path),
            IconSource = App.Symbol(FASymbol.Link),
            Footer = show,
        });
        if (shortcut is not null)
        {
            page.Children.Add(new FASettingsExpander
            {
                Header = "Opens",
                Description = (Explorer.Friendly(shortcut.Target) + " " + shortcut.Arguments).Trim(),
                IconSource = App.Symbol(FASymbol.Code),
            });
        }

        var remove = new Button { Content = $"Move to {Words.Bin}…" };
        ToolTip.SetTip(remove, "Delete");
        remove.Click += (_, _) => _ = owner.MoveLegacyToRecycleBin(path);
        page.Children.Add(new TextBlock { Text = "Remove", Classes = { "section" } });
        page.Children.Add(new FASettingsExpander
        {
            Header = $"Move to {Words.Bin}",
            Description = "Only this shortcut is removed. The app it opens is not changed.",
            IconSource = App.Symbol(FASymbol.Delete),
            Footer = remove,
        });
        return new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = page };
    }
}

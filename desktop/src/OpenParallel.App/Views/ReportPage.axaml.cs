using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using FluentAvalonia.UI.Controls;
using OpenParallel.App.Services;
using OpenParallel.Core;

namespace OpenParallel.App.Views;

/// <summary>What an instance's Report a problem button hands to the report page.</summary>
public sealed record ReportContext(string App, string Version, string Detail, ProblemKind Kind = ProblemKind.Separate);

/// <summary>Report a problem: build the report, read it, then send it from your own mail app (or
/// as a GitHub issue once the project is public), or copy it. Nothing is sent from here.</summary>
public partial class ReportPage : UserControl, IKeyAware
{
    private readonly ProjectInfo project = ProjectInfo.Load(AppContext.BaseDirectory);
    private readonly ProblemReport report = new();
    private bool shownMissing;

    public ReportPage(MainWindow owner, ReportContext? context = null)
    {
        InitializeComponent();
        AppCard.IconSource = App.Symbol(FASymbol.Document);
        VersionCard.IconSource = App.Symbol(FASymbol.Tag);
        SystemCard.IconSource = App.Symbol(FASymbol.Settings);
        PreviewText.FontFamily = App.Monospace;
        // The cards only frame their fields; Tab goes field to field.
        foreach (var card in new[] { AppCard, VersionCard, SystemCard }) card.IsTabStop = false;
        App.Dynamic(DetailsError, TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var system = OperatingSystem.IsWindows() ? (Platform.IsWine ? "Windows, in Wine" : "Windows") : "Linux";
        report.OpenParallelVersion = $"{(version is null ? project.Version ?? "development build" : $"{version.Major}.{version.Minor}.{version.Build}")} ({system})";
        SystemCard.Description = $"{report.System}  ·  OpenParallel {report.OpenParallelVersion}";

        if (context is not null)
        {
            AppBox.Text = context.App;
            VersionBox.Text = context.Version;
            report.AppDetail = context.Detail;
            AppCard.Description = $"From the instance: {context.Detail}.";
            foreach (var radio in Kinds.Children.OfType<RadioButton>()) radio.IsChecked = Equals(radio.Tag, context.Kind.ToString());
        }

        foreach (var radio in Kinds.Children.OfType<RadioButton>()) radio.IsCheckedChanged += (_, _) => Update();
        foreach (var box in new[] { AppBox, VersionBox, DetailsBox, StepsBox }) box.TextChanged += (_, _) => Update();
        IncludeToggle.IsCheckedChanged += (_, _) => Update();

        IssueButton.IsVisible = project.Repository is not null;
        EmailButton.IsEnabled = project.ReportAddress is not null;
        Note.Text = project.ReportAddress is null
            ? "The report address isn’t set in this build yet. Copy the report for now."
            : "Your mail app opens with this report filled in. Nothing is sent until you press Send there.";
        ToolTip.SetTip(EmailButton, project.ReportAddress is null ? "The project’s report address isn’t set in this build" : "Opens your mail app with this report");

        EmailButton.Click += async (_, _) => await SendAsync(() => report.MailUrl(project.ReportAddress!));
        IssueButton.Click += async (_, _) => await SendAsync(() => report.IssueUrl(project.Repository!));
        CopyButton.Click += async (_, _) =>
        {
            if (!Checked()) return;
            await CopyAsync();
            CopyButton.Content = "Copied";
            Avalonia.Threading.DispatcherTimer.RunOnce(() => CopyButton.Content = "Copy report", TimeSpan.FromSeconds(2));
        };

        Update();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => (context is null ? AppBox : DetailsBox).Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void Update()
    {
        var kind = Kinds.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;
        report.Kind = Enum.TryParse<ProblemKind>(kind, out var parsed) ? parsed : ProblemKind.Bug;
        report.App = AppBox.Text ?? "";
        report.AppVersion = VersionBox.Text ?? "";
        report.Details = DetailsBox.Text ?? "";
        report.Steps = StepsBox.Text ?? "";
        report.IncludeSystem = IncludeToggle.IsChecked == true;
        PreviewText.Text = report.Text;
        if (shownMissing && report.IsComplete) DetailsError.IsVisible = false;
    }

    private bool Checked()
    {
        Update();
        shownMissing = !report.IsComplete;
        DetailsError.IsVisible = shownMissing;
        if (shownMissing) DetailsBox.Focus();
        return report.IsComplete;
    }

    private async Task SendAsync(Func<string> url)
    {
        if (!Checked()) return;
        if (report.IsTrimmedForMail) await CopyAsync();
        Explorer.OpenUrl(url());
    }

    private async Task CopyAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(report.Text);
    }

    public bool HandleKey(Avalonia.Input.KeyEventArgs e, bool typing) => false;
}

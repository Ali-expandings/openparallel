using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpenParallel.Core;

/// <summary>Public project facts from <c>project.json</c> (shipped beside the app, shared with the
/// Mac app and the website). Empty values hide what needs them.</summary>
public sealed record ProjectInfo(string? Version = null, string? ReportEmail = null, string? RepositoryUrl = null)
{
    public static ProjectInfo Load(string folder)
    {
        try
        {
            var file = Path.Combine(folder, "project.json");
            return File.Exists(file) ? JsonSerializer.Deserialize<ProjectInfo>(File.ReadAllText(file), InstanceService.Json) ?? new ProjectInfo() : new ProjectInfo();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ProjectInfo();
        }
    }

    public string? ReportAddress => string.IsNullOrWhiteSpace(ReportEmail) ? null : ReportEmail.Trim();
    public string? Repository => string.IsNullOrWhiteSpace(RepositoryUrl) ? null : RepositoryUrl.Trim().TrimEnd('/');
}

public enum ProblemKind
{
    Bug,
    Separate,
    Idea,
    Other,
}

/// <summary>One report in the format every OpenParallel surface uses (Mac, Windows, Linux,
/// website). Nothing here sends anything: the person's own mail app or browser does.</summary>
public sealed class ProblemReport
{
    public const int MailLimit = 1800;

    public ProblemKind Kind { get; set; } = ProblemKind.Bug;
    public string App { get; set; } = "";
    public string AppVersion { get; set; } = "";
    /// <summary>Recipe family and status of the instance the report started from.</summary>
    public string AppDetail { get; set; } = "";
    public string Details { get; set; } = "";
    public string Steps { get; set; } = "";
    public bool IncludeSystem { get; set; } = true;
    public string System { get; set; } = SystemDescription();
    public string OpenParallelVersion { get; set; } = "";

    public static string Title(ProblemKind kind) => kind switch
    {
        ProblemKind.Bug => "Something doesn’t work",
        ProblemKind.Separate => "An app doesn’t keep its data separate",
        ProblemKind.Idea => "An idea or an app to support",
        _ => "Something else",
    };

    public static string SystemDescription()
    {
        var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var pretty = File.ReadLines("/etc/os-release").FirstOrDefault(l => l.StartsWith("PRETTY_NAME=", StringComparison.Ordinal));
                if (pretty is not null) return $"{pretty[12..].Trim('"')} ({arch})";
            }
            catch (IOException)
            {
            }
            return $"Linux ({arch})";
        }
        if (OperatingSystem.IsWindows()) return $"Windows {Environment.OSVersion.Version} ({arch})";
        return $"{RuntimeInformation.OSDescription} ({arch})";
    }

    public bool IsComplete => !string.IsNullOrWhiteSpace(Details);

    public string Subject
    {
        get
        {
            var name = App.Trim();
            return $"[OpenParallel] {Title(Kind)}{(name.Length == 0 ? "" : $": {name}")}";
        }
    }

    public string Body
    {
        get
        {
            var lines = new List<string> { $"What happened: {Title(Kind)}" };
            var app = string.Join(' ', new[] { App.Trim(), AppVersion.Trim() }.Where(s => s.Length > 0));
            if (app.Length > 0) lines.Add($"App: {app}{(AppDetail.Length > 0 ? $" ({AppDetail})" : "")}");
            if (IncludeSystem)
            {
                if (OpenParallelVersion.Length > 0) lines.Add($"OpenParallel: {OpenParallelVersion}");
                lines.Add($"System: {System}");
            }
            lines.Add("---");
            lines.Add(IsComplete ? Details.Trim() : "(what happened)");
            if (!string.IsNullOrWhiteSpace(Steps)) lines.AddRange(["", "How to see it again:", Steps.Trim()]);
            return string.Join("\n", lines);
        }
    }

    public string Text => $"Subject: {Subject}\n\n{Body}";

    public bool IsTrimmedForMail => Body.Length > MailLimit;

    /// <summary>A <c>mailto:</c> link. Mail apps drop long bodies, so a long report is trimmed there
    /// and the caller puts the full text on the clipboard.</summary>
    public string MailUrl(string address)
    {
        var body = IsTrimmedForMail ? Body[..MailLimit] + "\n\n(Trimmed. The full report is on your clipboard; paste it here.)" : Body;
        return $"mailto:{address}?subject={Uri.EscapeDataString(Subject)}&body={Uri.EscapeDataString(body)}";
    }

    public string IssueUrl(string repository) =>
        $"{repository.TrimEnd('/')}/issues/new?title={Uri.EscapeDataString(Subject.Replace("[OpenParallel] ", ""))}&body={Uri.EscapeDataString(Body)}";
}

using Xunit;

namespace OpenParallel.Core.Tests;

public class ReportTests
{
    [Fact]
    public void UsesTheSharedFormat()
    {
        var report = new ProblemReport
        {
            Kind = ProblemKind.Separate, App = "Discord", AppVersion = "0.0.407", AppDetail = "Electron, Supported",
            Details = "Both copies show my main account.", Steps = "1. Open Discord Copy",
            System = "Windows 10.0.19045 (x64)", OpenParallelVersion = "1.0.0 (Windows)",
        };
        Assert.Equal("[OpenParallel] An app doesn’t keep its data separate: Discord", report.Subject);
        Assert.Equal("What happened: An app doesn’t keep its data separate\nApp: Discord 0.0.407 (Electron, Supported)\n" +
                     "OpenParallel: 1.0.0 (Windows)\nSystem: Windows 10.0.19045 (x64)\n---\nBoth copies show my main account.\n\n" +
                     "How to see it again:\n1. Open Discord Copy", report.Body);
        report.IncludeSystem = false;
        Assert.DoesNotContain("System:", report.Body);
    }

    [Fact]
    public void BuildsMailAndIssueLinksAndTrimsLongMail()
    {
        var report = new ProblemReport { Details = "It crashed & burned" };
        var mail = report.MailUrl("reports@example.org");
        Assert.StartsWith("mailto:reports@example.org?subject=", mail);
        Assert.Contains("crashed%20%26%20burned", mail);
        Assert.StartsWith("https://github.com/x/op/issues/new?title=", report.IssueUrl("https://github.com/x/op/"));
        report.Details = new string('x', ProblemReport.MailLimit + 50);
        Assert.True(report.IsTrimmedForMail);
        Assert.Contains("Trimmed", report.MailUrl("a@b.c"));
        Assert.False(new ProblemReport().IsComplete);
    }

    [Fact]
    public void ReadsProjectFactsAndTreatsEmptyAsUnset()
    {
        var dir = Path.Combine(Path.GetTempPath(), "op-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "project.json"), """{ "version": "1.0.0", "reportEmail": "", "repositoryUrl": "https://github.com/x/op/" }""");
        var info = ProjectInfo.Load(dir);
        Assert.Null(info.ReportAddress);
        Assert.Equal("https://github.com/x/op", info.Repository);
        Assert.Equal("1.0.0", info.Version);
        Assert.Null(ProjectInfo.Load(Path.Combine(dir, "missing")).Repository);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void ReadsTheAuthorAndOnlyItsWebLinks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "op-author-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "project.json"), """
            { "author": { "signature": " Made by A ", "links": [
              { "label": "Site", "url": "https://a.example" }, { "label": "Mail", "url": "mailto:a@b.c" }, { "label": "", "url": "https://b.example" } ] } }
            """);
        var info = ProjectInfo.Load(dir);
        Assert.Equal("Made by A", info.Signature);
        Assert.Equal([("Site", "https://a.example")], info.AuthorLinks);
        Assert.Null(new ProjectInfo().Signature);
        Assert.Empty(new ProjectInfo().AuthorLinks);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void ShippedProjectFactsCarryTheSignature()
    {
        // project.json at the repository root is the file every app and the website read.
        var info = ProjectInfo.Load(RepositoryRoot());
        Assert.Equal("Made by Ali Assem, The Tech Butcher", info.Signature);
        Assert.Equal(["GitHub", "LinkedIn", "Instagram", "Linktree"], info.AuthorLinks.Select(l => l.Label));
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
}

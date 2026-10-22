using System.Text.Json;
using OpenParallel.Core;
using OpenParallel.Core.Linux;
using OpenParallel.Core.Windows;

namespace OpenParallel.Cli;

/// <summary>opctl: every OpenParallel operation from the command line, with JSON output for
/// scripts and tests.</summary>
internal static class Program
{
    private const string Usage = """
        opctl — OpenParallel command line (Windows and Linux)

          opctl inspect <app.exe | app.desktop | program> [--json]
          opctl apps [--json]                     installed apps OpenParallel can see
          opctl browsers                          browsers that can open website instances
          opctl create --app <app.exe> [--name N] [--shared] [--arg A]... [--env K=V]... [--desktop]
          opctl create --web <url> [--name N] [--browser <exe>]
          opctl list [--json]
          opctl launch <slug>
          opctl running [--json]
          opctl show <slug>
          opctl quit <slug> [--force]
          opctl trash <slug>                      move to the Recycle Bin / Trash
          opctl shortcut <path.lnk|.desktop> --target <exe> [--args "..."] [--name N]
          opctl read-shortcut <path.lnk|.desktop>
        """;

    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("opctl runs on Windows (or in Wine) and Linux.");
            return 2;
        }
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        IPlatform platform = OperatingSystem.IsWindows() ? new WindowsPlatform() : new LinuxPlatform();
        var service = new InstanceService(platform);
        try
        {
            return args[0] switch
            {
                "inspect" => Inspect(service, args),
                "apps" => Apps(service, args),
                "browsers" => Print(platform.Apps.Browsers(platform.Apps.InstalledApps(platform).Select(a => platform.Apps.Inspect(a.Path)))),
                "create" => Create(service, args),
                "list" => List(service, args),
                "launch" => Launch(service, args),
                "running" => Running(service, args),
                "show" => Show(service, args),
                "quit" => Quit(service, args),
                "trash" => Trash(service, args),
                "shortcut" => MakeShortcut(service, args),
                "read-shortcut" => Print(service.Platform.ReadShortcut(Arg(args, 1))),
                _ => Fail($"Unknown command: {args[0]}\n\n{Usage}"),
            };
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
        {
            return Fail(error.Message);
        }
    }

    private static int Inspect(InstanceService service, string[] args) => Print(service.Inspect(Arg(args, 1)));

    private static int Apps(InstanceService service, string[] args)
    {
        var apps = service.Platform.Apps.InstalledApps(service.Platform)
            .Select(a => new { a.Name, a.ExePath, Inspected = service.Inspect(a.ExePath) })
            .Select(x => new { x.Name, x.ExePath, Family = x.Inspected.Family.ToString(), Status = x.Inspected.Status.ToString() });
        return Print(apps);
    }

    private static int Create(InstanceService service, string[] args)
    {
        var app = Option(args, "--app");
        var web = Option(args, "--web");
        var environment = Options(args, "--env").Select(e => e.Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");
        InstanceManifest manifest;
        if (web is not null)
        {
            var known = service.Platform.Apps.InstalledApps(service.Platform).Select(a => service.Inspect(a.Path));
            var browser = Option(args, "--browser") ?? service.Platform.Apps.Browsers(known).FirstOrDefault()
                ?? throw new InvalidOperationException("No Chromium browser found for a website instance.");
            var host = Uri.TryCreate(web, UriKind.Absolute, out var uri) ? uri.Host : web;
            manifest = service.Create(new InstanceRequest { Name = Option(args, "--name") ?? host, AppPath = browser, WebUrl = web });
        }
        else
        {
            if (app is null) return Fail("create needs --app <exe> or --web <url>");
            var inspected = service.Inspect(app);
            manifest = service.Create(new InstanceRequest
            {
                Name = Option(args, "--name") ?? service.SuggestName(inspected.Name),
                AppPath = app,
                SeparateData = !args.Contains("--shared"),
                ExtraArguments = Options(args, "--arg").ToList(),
                Environment = environment,
                DesktopShortcut = args.Contains("--desktop"),
            }, inspected);
        }
        return Print(manifest);
    }

    private static int List(InstanceService service, string[] args) => Print(service.List());

    private static int Launch(InstanceService service, string[] args)
    {
        var manifest = Find(service, Arg(args, 1));
        return Print(new { slug = manifest.InstanceSlug, pid = service.Launch(manifest) });
    }

    private static int Running(InstanceService service, string[] args) => Print(service.Running());

    private static int Show(InstanceService service, string[] args)
    {
        var manifest = Find(service, Arg(args, 1));
        service.OpenOrShow(manifest);
        return Print(new { slug = manifest.InstanceSlug, running = service.RunningPids(manifest) });
    }

    private static int Quit(InstanceService service, string[] args)
    {
        var manifest = Find(service, Arg(args, 1));
        if (args.Contains("--force"))
        {
            service.ForceQuit(manifest);
            return Print(new { slug = manifest.InstanceSlug, forced = true });
        }
        return Print(new { slug = manifest.InstanceSlug, asked = service.Quit(manifest) });
    }

    private static int Trash(InstanceService service, string[] args)
    {
        var manifest = Find(service, Arg(args, 1));
        var paths = service.OwnedPaths(manifest);
        service.MoveToRecycleBin(manifest);
        return Print(new { slug = manifest.InstanceSlug, recycled = paths });
    }

    private static int MakeShortcut(InstanceService service, string[] args)
    {
        var path = Arg(args, 1);
        var target = Option(args, "--target") ?? throw new ArgumentException("shortcut needs --target <exe>");
        service.Platform.WriteShortcut(new ShortcutDefinition
        {
            Path = path, Target = target, Arguments = Option(args, "--args") ?? "",
            ArgumentList = service.Platform.SplitArguments(Option(args, "--args") ?? ""),
            Name = Option(args, "--name"), WorkingDirectory = Path.GetDirectoryName(target), IconPath = target,
        });
        return Print(service.Platform.ReadShortcut(path));
    }

    // MARK: helpers

    private static InstanceManifest Find(InstanceService service, string slug) =>
        service.Find(slug) ?? throw new ArgumentException($"No instance named {slug}. Run: opctl list");

    private static string Arg(string[] args, int index) =>
        args.Length > index ? args[index] : throw new ArgumentException($"Missing argument.\n\n{Usage}");

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static IEnumerable<string> Options(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name) yield return args[i + 1];
    }

    private static int Print(object? value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, InstanceService.Json));
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }
}

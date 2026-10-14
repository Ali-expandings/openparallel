namespace OpenParallel.Core;

/// <summary>Which running process belongs to which instance. The Mac rule, unchanged: the
/// process runs from the app's folder and its command line or environment points inside that
/// instance's data folder, followed by a separator, so <c>claude</c> never matches
/// <c>claude-2</c>.</summary>
public static class ProcessMatcher
{
    /// <param name="UsesEnvironment">The recipe passes the data folder in an environment
    /// variable, so a process without it on its command line may still be this instance.</param>
    public sealed record Target(string Slug, string ProcessFolder, string Marker, bool UsesEnvironment = true);

    public static IReadOnlyList<Target> Targets(IEnumerable<InstanceManifest> instances, Func<InstanceManifest, string> dataRootOf) =>
        instances
            .Where(m => m.ProfileIsolation && !string.IsNullOrEmpty(m.ProcessFolder))
            .Select(m => new Target(m.InstanceSlug, WithSeparator(m.ProcessFolder!), WithSeparator(dataRootOf(m)), m.Environment.Count > 0))
            .ToList();

    /// <summary>Matches on command lines. Returns the matches and the PIDs that run from an
    /// instance's folder but did not name its data folder, whose environment must be checked.</summary>
    public static (Dictionary<string, List<int>> Running, List<int> NeedsEnvironment) Match(
        IReadOnlyList<Target> targets, IEnumerable<ProcessSnapshot> processes)
    {
        var running = new Dictionary<string, List<int>>();
        var pending = new List<int>();
        foreach (var process in processes)
        {
            if (IsHelperProcess(process.CommandLine)) continue;
            var candidates = targets.Where(t => process.ImagePath.StartsWith(t.ProcessFolder, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 0) continue;
            var match = candidates.FirstOrDefault(t => process.CommandLine.Contains(t.Marker, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                Add(running, match.Slug, process.Pid);
            }
            else if (candidates.Any(t => t.UsesEnvironment))
            {
                pending.Add(process.Pid);
            }
        }
        return (running, pending);
    }

    /// <summary>Adds environment-only matches (recipes that pass the data folder in an
    /// environment variable rather than an argument).</summary>
    public static void MatchEnvironment(IReadOnlyList<Target> targets, int pid, string imagePath, string environment,
        Dictionary<string, List<int>> running)
    {
        var target = targets.FirstOrDefault(t =>
            imagePath.StartsWith(t.ProcessFolder, StringComparison.OrdinalIgnoreCase)
            && environment.Contains(t.Marker, StringComparison.OrdinalIgnoreCase));
        if (target is not null) Add(running, target.Slug, pid);
    }

    /// <summary>Linux: an instance runs when a process carries its exact profile arguments
    /// (<c>--user-data-dir=&lt;path&gt;</c>, <c>-profile &lt;path&gt;</c>…), or, for recipes that use the
    /// environment, its exact <c>KEY=value</c>. Children that inherit them are folded into the
    /// topmost matching process, and helpers are skipped.</summary>
    public static Dictionary<string, List<int>> MatchArguments(IReadOnlyList<InstanceManifest> instances,
        Func<InstanceManifest, string> dataRootOf, IReadOnlyList<ProcessSnapshot> processes, Func<int, string?> environmentOf)
    {
        var signatures = instances
            .Where(m => m.ProfileIsolation)
            .Select(m => (Manifest: m, Arguments: SignatureArguments(m, dataRootOf(m)), Environment: SignatureEnvironment(m, dataRootOf(m))))
            .Where(s => s.Arguments.Count > 0 || s.Environment.Count > 0)
            .ToList();
        var matched = new Dictionary<int, string>();
        if (signatures.Count == 0) return [];
        var byArguments = signatures.Where(s => s.Arguments.Count > 0).ToList();
        var byEnvironment = signatures.Where(s => s.Arguments.Count == 0).ToList();
        foreach (var process in processes)
        {
            if (process.Arguments is not { Count: > 0 } argv || IsHelperProcess(argv)) continue;
            var hit = byArguments.FirstOrDefault(s => s.Arguments.All(argv.Contains));
            if (hit.Manifest is not null)
            {
                matched[process.Pid] = hit.Manifest.InstanceSlug;
                continue;
            }
            if (byEnvironment.Count == 0) continue;
            var environment = environmentOf(process.Pid);
            if (environment is null) continue;
            var entries = environment.Split('\0');
            var envHit = byEnvironment.FirstOrDefault(s => s.Environment.All(entries.Contains));
            if (envHit.Manifest is not null) matched[process.Pid] = envHit.Manifest.InstanceSlug;
        }
        // Only the topmost process of each instance: children inherit arguments' environment.
        var parents = processes.ToDictionary(p => p.Pid, p => p.ParentPid);
        var running = new Dictionary<string, List<int>>();
        foreach (var (pid, slug) in matched)
        {
            var parent = parents.GetValueOrDefault(pid);
            if (parent != 0 && matched.TryGetValue(parent, out var parentSlug) && parentSlug == slug) continue;
            Add(running, slug, pid);
        }
        return running;
    }

    /// <summary>The launch arguments that name the instance's data, with the switch before a
    /// separate value. Wrapper options (<c>flatpak --filesystem=</c>, <c>--env=</c>) are not
    /// part of the app's own arguments, so they are left out.</summary>
    public static IReadOnlyList<string> SignatureArguments(InstanceManifest manifest, string dataRoot)
    {
        var roots = new[] { dataRoot, manifest.ExtraDataPath }.Where(r => !string.IsNullOrEmpty(r)).Select(r => r!).ToList();
        var args = manifest.LaunchArguments;
        var result = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--filesystem=", StringComparison.Ordinal) || arg.StartsWith("--env=", StringComparison.Ordinal)) continue;
            if (!roots.Any(r => arg.Contains(r, StringComparison.Ordinal))) continue;
            if (!arg.StartsWith('-') && i > 0 && args[i - 1].StartsWith('-')) result.Add(args[i - 1]);
            result.Add(arg);
        }
        return result;
    }

    public static IReadOnlyList<string> SignatureEnvironment(InstanceManifest manifest, string dataRoot) =>
        manifest.Environment
            .Where(e => e.Value.Contains(dataRoot, StringComparison.Ordinal)
                        || (manifest.ExtraDataPath is { } extra && e.Value.Contains(extra, StringComparison.Ordinal)))
            .Select(e => $"{e.Key}={e.Value}")
            .ToList();

    /// <summary>Linux helpers: Chromium/Electron <c>--type=</c> children, Firefox content processes.</summary>
    public static bool IsHelperProcess(IReadOnlyList<string> argv) =>
        argv.Any(a => a.StartsWith("--type=", StringComparison.Ordinal)) || argv.Contains("-contentproc");

    /// <summary>Chromium and Electron start renderer, GPU and utility processes from the same
    /// exe with <c>--type=</c>; only the main process represents the instance.</summary>
    public static bool IsHelperProcess(string commandLine) =>
        commandLine.Contains(" --type=", StringComparison.Ordinal) || commandLine.Contains("\"--type=", StringComparison.Ordinal);

    private static void Add(Dictionary<string, List<int>> map, string slug, int pid)
    {
        if (!map.TryGetValue(slug, out var list)) map[slug] = list = [];
        list.Add(pid);
    }

    private static string WithSeparator(string path) =>
        path.EndsWith('\\') || path.EndsWith('/') ? path : path + (path.Contains('\\') ? "\\" : "/");
}

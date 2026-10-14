using System.Runtime.InteropServices;
using System.Text.Json;

// A stand-in for real Linux apps in the end-to-end tests. Copied (or symlinked) under the name
// of the app it plays; it records how it was started, then waits until it is asked to quit
// (SIGTERM, like closing an app from its menu) and records that too.
var logDir = Environment.GetEnvironmentVariable("OP_FIXTURE_LOG") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "op-fixtures");
Directory.CreateDirectory(logDir);
var pid = Environment.ProcessId;
var logFile = Path.Combine(logDir, $"{pid}.json");

void Log(object record) => File.AppendAllText(logFile, JsonSerializer.Serialize(record) + "\n");

var environment = Environment.GetEnvironmentVariables().Keys.Cast<string>()
    .Where(k => k.StartsWith("XDG_", StringComparison.Ordinal) && k is not ("XDG_RUNTIME_DIR" or "XDG_SESSION_ID" or "XDG_SESSION_TYPE"
        or "XDG_SESSION_CLASS" or "XDG_SESSION_DESKTOP" or "XDG_CURRENT_DESKTOP" or "XDG_DATA_DIRS" or "XDG_CONFIG_DIRS" or "XDG_MENU_PREFIX")
        || k.EndsWith("_PROPERTIES", StringComparison.Ordinal) || k.StartsWith("OP_", StringComparison.Ordinal))
    .OrderBy(k => k)
    .ToDictionary(k => k, k => Environment.GetEnvironmentVariable(k));
var argv = Environment.GetCommandLineArgs();
string? Profile()
{
    for (var i = 1; i < argv.Length; i++)
    {
        if (argv[i].StartsWith("--user-data-dir=", StringComparison.Ordinal)) return argv[i]["--user-data-dir=".Length..];
        if (argv[i] is "--user-data-dir" or "-profile" or "-workdir" && i + 1 < argv.Length) return argv[i + 1];
    }
    return environment.FirstOrDefault(e => e.Key.EndsWith("_PROPERTIES", StringComparison.Ordinal) || e.Key == "XDG_CONFIG_HOME").Value;
}

Log(new { @event = "start", pid, exe = Environment.ProcessPath, name = Path.GetFileName(Environment.ProcessPath), args = argv.Skip(1), profile = Profile(), cwd = Environment.CurrentDirectory, environment });

using var quit = new ManualResetEventSlim();
using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    quit.Set();
});
quit.Wait(TimeSpan.FromMinutes(30));
Log(new { @event = "closed", pid });

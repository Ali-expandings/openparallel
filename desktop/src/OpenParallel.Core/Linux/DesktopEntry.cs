using System.Text;

namespace OpenParallel.Core.Linux;

/// <summary>A freedesktop.org desktop entry (<c>.desktop</c> file): reading the
/// <c>[Desktop Entry]</c> group, and writing OpenParallel's shortcuts, per the Desktop Entry
/// Specification 1.5 (string escapes, then <c>Exec</c> quoting, then <c>%</c> field codes).</summary>
public sealed class DesktopEntry
{
    private static readonly string[] FieldCodes = ["%f", "%F", "%u", "%U", "%i", "%c", "%k", "%d", "%D", "%n", "%N", "%v", "%m"];
    private const string Reserved = " \t\n\"'\\><~|&;$*?#()`";

    public string Path { get; }
    public IReadOnlyDictionary<string, string> Values { get; }

    private DesktopEntry(string path, Dictionary<string, string> values)
    {
        Path = path;
        Values = values;
    }

    public string? this[string key] => Values.TryGetValue(key, out var value) ? value : null;
    public string Name => this["Name"] ?? System.IO.Path.GetFileNameWithoutExtension(Path);
    public bool Flag(string key) => string.Equals(this[key], "true", StringComparison.OrdinalIgnoreCase);

    public static DesktopEntry? Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path), path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Only the <c>[Desktop Entry]</c> group; localized keys (<c>Name[ar]</c>) are skipped.</summary>
    public static DesktopEntry Parse(string text, string path = "")
    {
        var values = new Dictionary<string, string>();
        var inMain = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith('['))
            {
                inMain = line.Trim() == "[Desktop Entry]";
                continue;
            }
            if (!inMain) continue;
            var equals = line.IndexOf('=');
            if (equals <= 0) continue;
            var key = line[..equals].Trim();
            if (key.Contains('[')) continue;
            values.TryAdd(key, Unescape(line[(equals + 1)..].TrimStart()));
        }
        return new DesktopEntry(path, values);
    }

    /// <summary>The string-type escapes: <c>\s \n \t \r \\</c>.</summary>
    public static string Unescape(string value)
    {
        if (!value.Contains('\\')) return value;
        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                result.Append(next switch { 's' => ' ', 'n' => '\n', 't' => '\t', 'r' => '\r', '\\' => '\\', _ => next });
                if (next is not ('s' or 'n' or 't' or 'r' or '\\')) result.Insert(result.Length - 1, '\\');
            }
            else
            {
                result.Append(value[i]);
            }
        }
        return result.ToString();
    }

    public static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r");

    /// <summary>An unescaped <c>Exec</c> value → the command and its arguments. Field codes
    /// (<c>%U</c>…) and Flatpak's <c>@@</c> markers are dropped; <c>%%</c> becomes <c>%</c>.</summary>
    public static IReadOnlyList<string> SplitExec(string exec)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inToken = false;
        var quoted = false;
        for (var i = 0; i < exec.Length; i++)
        {
            var c = exec[i];
            if (quoted)
            {
                if (c == '\\' && i + 1 < exec.Length && "\"`$\\".Contains(exec[i + 1])) current.Append(exec[++i]);
                else if (c == '"') quoted = false;
                else current.Append(c);
            }
            else if (c is ' ' or '\t' or '\n')
            {
                if (inToken) tokens.Add(current.ToString());
                current.Clear();
                inToken = false;
            }
            else
            {
                inToken = true;
                if (c == '"') quoted = true;
                else current.Append(c);
            }
        }
        if (inToken) tokens.Add(current.ToString());

        var result = new List<string>();
        foreach (var token in tokens)
        {
            if (FieldCodes.Contains(token) || token is "@@" or "@@u" or "@@f") continue;
            var text = token.Replace("%%", "\u0000");
            foreach (var code in FieldCodes) text = text.Replace(code, "");
            result.Add(text.Replace("\u0000", "%"));
        }
        return result;
    }

    /// <summary>One argument for <c>Exec</c> (before the string escape is applied).</summary>
    public static string QuoteExecArgument(string argument)
    {
        var text = argument.Replace("%", "%%");
        if (text.Length > 0 && !text.Any(c => Reserved.Contains(c))) return text;
        var quoted = new StringBuilder("\"");
        foreach (var c in text)
        {
            if (c is '"' or '`' or '$' or '\\') quoted.Append('\\');
            quoted.Append(c);
        }
        return quoted.Append('"').ToString();
    }

    public static string JoinExec(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(QuoteExecArgument));

    /// <summary>The command of an <c>Exec</c> with a leading <c>env K=V …</c> taken out as environment.</summary>
    public static (IReadOnlyList<string> Command, IReadOnlyDictionary<string, string> Environment) SplitEnvironment(IReadOnlyList<string> exec)
    {
        var environment = new Dictionary<string, string>();
        var start = 0;
        if (exec.Count > 0 && (exec[0] == "env" || exec[0].EndsWith("/env", StringComparison.Ordinal)))
        {
            start = 1;
            while (start < exec.Count && exec[start].IndexOf('=') is > 0 and var equals && !exec[start].StartsWith('-'))
            {
                environment[exec[start][..equals]] = exec[start][(equals + 1)..];
                start++;
            }
        }
        return (exec.Skip(start).ToList(), environment);
    }

    /// <summary>OpenParallel's shortcut for an instance.</summary>
    public static string Write(ShortcutDefinition shortcut)
    {
        var exec = new List<string>();
        if (shortcut.Environment.Count > 0)
        {
            exec.Add("env");
            exec.AddRange(shortcut.Environment.Select(e => $"{e.Key}={e.Value}"));
        }
        exec.Add(shortcut.Target);
        exec.AddRange(shortcut.ArgumentList);

        var text = new StringBuilder();
        text.Append("[Desktop Entry]\n");
        text.Append("Type=Application\n");
        text.Append("Version=1.5\n");
        text.Append("Name=").Append(Escape(shortcut.Name ?? System.IO.Path.GetFileNameWithoutExtension(shortcut.Path))).Append('\n');
        if (shortcut.Description is { Length: > 0 } description) text.Append("Comment=").Append(Escape(description)).Append('\n');
        text.Append("Exec=").Append(Escape(JoinExec(exec))).Append('\n');
        if (shortcut.WorkingDirectory is { Length: > 0 } workDir) text.Append("Path=").Append(Escape(workDir)).Append('\n');
        if (shortcut.IconPath is { Length: > 0 } icon) text.Append("Icon=").Append(Escape(icon)).Append('\n');
        text.Append("Terminal=false\n");
        text.Append("StartupNotify=true\n");
        if (shortcut.WindowClass is { Length: > 0 } windowClass) text.Append("StartupWMClass=").Append(Escape(windowClass)).Append('\n');
        text.Append("X-OpenParallel=true\n");
        return text.ToString();
    }

    /// <summary>Reads a shortcut back into a <see cref="ShortcutDefinition"/>.</summary>
    public ShortcutDefinition ToShortcut()
    {
        var (command, environment) = SplitEnvironment(SplitExec(this["Exec"] ?? ""));
        return new ShortcutDefinition
        {
            Path = Path,
            Name = Name,
            Target = command.FirstOrDefault() ?? "",
            ArgumentList = command.Skip(1).ToList(),
            Arguments = PosixCommandLine.Join(command.Skip(1)),
            Environment = environment,
            WorkingDirectory = this["Path"],
            IconPath = this["Icon"],
            Description = this["Comment"],
            WindowClass = this["StartupWMClass"],
        };
    }
}

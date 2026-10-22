using System.Globalization;
using System.Text;

namespace OpenParallel.Core;

public static class Naming
{
    /// <summary>Folder-safe id for an instance name. Identical to the Mac app's rule, so the
    /// same name gives the same slug on both platforms: ASCII letters and digits stay, other
    /// ASCII becomes a dash, anything else becomes <c>-u{hex}-</c>, dashes collapse.</summary>
    public static string Slug(string value)
    {
        var mapped = new StringBuilder();
        foreach (var rune in value.ToLowerInvariant().EnumerateRunes())
        {
            var v = rune.Value;
            if (v is >= '0' and <= '9' or >= 'a' and <= 'z')
                mapped.Append((char)v);
            else if (v < 128)
                mapped.Append('-');
            else
                mapped.Append("-u").Append(v.ToString("x")).Append('-');
        }
        var slug = string.Join('-', mapped.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "shortcut" : slug;
    }

    /// <summary>The default name for a new instance of an app: <c>&lt;App&gt; 2</c>, the original
    /// being number one. <see cref="InstanceService.SuggestName"/> picks the next free number.</summary>
    public static string DefaultInstanceName(string appName) => $"{appName.Trim()} 2";

    /// <summary>Names to try, in order: the name itself, then the next numbers. A name that
    /// already ends in a small number counts on from it ("Discord 2" → "Discord 3"); any other
    /// name gets " 2", " 3", … ("Office 365" → "Office 365 2"). Same rule as the Mac app.</summary>
    public static IEnumerable<string> Candidates(string name)
    {
        var baseName = name;
        var number = 1;
        var space = name.LastIndexOf(' ');
        if (space > 0 && int.TryParse(name[(space + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 2 and <= 99)
        {
            baseName = name[..space];
            number = n;
        }
        for (; ; number++) yield return number == 1 ? baseName : $"{baseName} {number}";
    }

    /// <summary>A name that is safe as a Windows file name (for the <c>.lnk</c>).</summary>
    public static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var cleaned = new string(value.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "Instance" : cleaned;
    }
}

/// <summary>Builds a Windows command line that <c>CommandLineToArgvW</c> (and every C runtime)
/// splits back into exactly the original arguments.</summary>
public static class WindowsCommandLine
{
    public static string Join(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            return argument;

        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
            {
                result.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                result.Append('\\', backslashes).Append(c);
            }
            backslashes = 0;
        }
        result.Append('\\', backslashes * 2).Append('"');
        return result.ToString();
    }

    /// <summary>Splits a command line the way <c>CommandLineToArgvW</c> does (used for
    /// user-typed launch arguments and for reading shortcuts).</summary>
    public static IReadOnlyList<string> Split(string commandLine)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (c == '\\')
            {
                var start = i;
                while (i < commandLine.Length && commandLine[i] == '\\') i++;
                var count = i - start;
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    current.Append('\\', count / 2);
                    if (count % 2 == 1) { current.Append('"'); }
                    else { inQuotes = !inQuotes; }
                }
                else
                {
                    current.Append('\\', count);
                    i--;
                }
                hasToken = true;
            }
            else if (c == '"')
            {
                if (inQuotes && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                hasToken = true;
            }
            else if ((c == ' ' || c == '\t') && !inQuotes)
            {
                if (hasToken) { args.Add(current.ToString()); current.Clear(); hasToken = false; }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }
        if (hasToken) args.Add(current.ToString());
        return args;
    }
}

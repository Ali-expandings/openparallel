namespace OpenParallel.Core.Linux;

/// <summary>Arguments as a POSIX shell writes them: for display, Copy command, and the
/// Advanced "Launch arguments" box.</summary>
public static class PosixCommandLine
{
    private const string Special = " \t\n\"'\\$`|&;<>()*?[]#~!";

    public static string Quote(string argument) =>
        argument.Length > 0 && argument.All(c => !Special.Contains(c))
            ? argument
            : "'" + argument.Replace("'", "'\\''") + "'";

    public static string Join(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    /// <summary>Splits like a shell: whitespace separates, '…' is literal, "…" honours \ before
    /// <c>" \ $ `</c>, and a backslash outside quotes escapes the next character.</summary>
    public static IReadOnlyList<string> Split(string text)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inToken = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (inToken) result.Add(current.ToString());
                current.Clear();
                inToken = false;
                continue;
            }
            inToken = true;
            if (c == '\'')
            {
                var end = text.IndexOf('\'', i + 1);
                if (end < 0) end = text.Length;
                current.Append(text, i + 1, end - i - 1);
                i = end;
            }
            else if (c == '"')
            {
                for (i++; i < text.Length && text[i] != '"'; i++)
                {
                    if (text[i] == '\\' && i + 1 < text.Length && "\"\\$`".Contains(text[i + 1])) i++;
                    current.Append(text[i]);
                }
            }
            else if (c == '\\' && i + 1 < text.Length)
            {
                current.Append(text[++i]);
            }
            else
            {
                current.Append(c);
            }
        }
        if (inToken) result.Add(current.ToString());
        return result;
    }
}

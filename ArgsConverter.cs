using System.Text;
using System.Text.RegularExpressions;

namespace GgufLauncher;

/// <summary>
/// Formats bash-style .args text into a clean, one-flag-per-row layout suitable for the
/// launcher's .args reader. Each flag (with its values) lands on its own line; regular line
/// breaks and comment lines are preserved. No caret (`^`) characters are emitted.
/// </summary>
public static class ArgsConverter
{
    // Match a backslash, optional horizontal whitespace, the line break, and optional leading
    // whitespace on the next line. Replace all with a single space so the continuation joins
    // the previous line as one .args line.
    private static readonly Regex LineContinuation = new(@"\\\s*\r?\n\s*");

    // Collapse horizontal whitespace runs created by joining continuations; newlines preserved.
    private static readonly Regex HorizontalWhitespace = new(@"[ \t]{2,}");

    /// <summary>
    /// Returns <paramref name="text"/> formatted with one flag per row: backslash line
    /// continuations are joined onto the previous line, each flag (with its values) starts a
    /// new row, and comment lines are preserved as-is. Null or empty input returns an empty
    /// string. Text without any continuations is returned unchanged.
    /// </summary>
    public static string FormatArgs(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string expanded = LineContinuation.Replace(text, " ");

        expanded = HorizontalWhitespace.Replace(expanded, " ");

        string[] rawLines = expanded.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var result = new List<string>();
        var current = new List<string>();

        foreach (string rawLine in rawLines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('#'))
            {
                if (current.Count > 0)
                {
                    result.Add(string.Join(" ", current));
                    current.Clear();
                }
                result.Add(line);
                continue;
            }

            foreach (string token in Tokenize(line))
            {
                if (IsFlag(token))
                {
                    if (current.Count > 0)
                    {
                        result.Add(string.Join(" ", current));
                    }
                    current = new List<string> { token };
                }
                else
                {
                    current.Add(token);
                }
            }
        }

        if (current.Count > 0)
        {
            result.Add(string.Join(" ", current));
        }

        result = RemoveDuplicateFlags(result);
        return string.Join(Environment.NewLine, result);
    }

    /// <summary>
    /// Removes duplicate non-repeatable flags, keeping each flag's first-occurrence position
    /// and its last-occurrence values ("later flags win"). Repeatable flags (LoRAs, control
    /// vectors, --logit-bias, ...) keep every occurrence. Comment lines and lines that do not
    /// start with a flag are left untouched. The relative order of distinct flags is preserved.
    /// </summary>
    private static List<string> RemoveDuplicateFlags(List<string> lines)
    {
        var units = new List<(string? Flag, string[] Values, string Original)>(lines.Count);
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                units.Add((null, [], line));
                continue;
            }

            string[] tokens = Tokenize(trimmed).ToArray();
            if (tokens.Length == 0 || !IsFlag(tokens[0]))
            {
                units.Add((null, tokens, line));
                continue;
            }

            units.Add((tokens[0], tokens[1..], line));
        }

        // Last occurrence index of every non-repeatable flag; its values win.
        Dictionary<string, int> lastIndex = new(StringComparer.Ordinal);
        for (int i = 0; i < units.Count; i++)
        {
            (string? flag, _, _) = units[i];
            if (flag is string f && !LauncherArgs.RepeatableFlags.Contains(f))
            {
                lastIndex[f] = i;
            }
        }

        var deduped = new List<string>(units.Count);
        for (int i = 0; i < units.Count; i++)
        {
            (string? flag, string[] values, string original) = units[i];

            if (flag is null)
            {
                // Comment or flagless line: keep verbatim.
                deduped.Add(original);
                continue;
            }

            if (!LauncherArgs.RepeatableFlags.Contains(flag) && lastIndex[flag] != i)
            {
                // Earlier duplicate of a non-repeatable flag: the last occurrence wins.
                continue;
            }

            deduped.Add(values.Length == 0 ? flag : flag + " " + string.Join(" ", values));
        }

        return deduped;
    }

    private static List<string> Tokenize(string line)
    {
        var parts = new List<string>();
        var token = new StringBuilder();
        bool inQuotes = false;
        bool tokenStarted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char current = line[i];
            if (inQuotes && current == '\\' && i + 1 < line.Length &&
                (line[i + 1] == '\\' || line[i + 1] == '"'))
            {
                token.Append(line[++i]);
                tokenStarted = true;
            }
            else if (current == '"')
            {
                inQuotes = !inQuotes;
                token.Append(current);
                tokenStarted = true;
            }
            else if (char.IsWhiteSpace(current) && !inQuotes)
            {
                if (tokenStarted)
                {
                    parts.Add(token.ToString());
                    token.Clear();
                    tokenStarted = false;
                }
            }
            else
            {
                token.Append(current);
                tokenStarted = true;
            }
        }

        if (tokenStarted)
        {
            parts.Add(token.ToString());
        }

        return parts;
    }

    private static bool IsFlag(string token)
    {
        return token.Length > 1 && token[0] == '-' && (token[1] == '-' || char.IsLetter(token[1]));
    }
}
using Axiomarium.Core.Evals;

namespace Axiomarium.Core.Evidence;

/// <summary>Which commands count as running a check.</summary>
public static class EvidenceCommands
{
    /// <summary>
    /// Whether <paramref name="command"/> counts as running <paramref name="check"/>: it's one command, and its words
    /// start with one of the check's <c>run</c> entries, word by word. The first word matches by the program it names,
    /// so a full path or a <c>.exe</c> counts too, and PowerShell's call operator in front is skipped. A narrowed run,
    /// such as <c>dotnet test --filter X</c>, counts: what it ran is kept with its record.
    /// </summary>
    /// <param name="check">The check.</param>
    /// <param name="command">A command line, as an agent or a user typed it.</param>
    /// <returns>Whether the command counts. A compound command never does.</returns>
    public static bool Counts(EvidenceCheck check, string command)
    {
        if (IsCompound(command))
        {
            return false;
        }

        var words = CommandLine.Words(command);
        if (words is ["&", .. var rest])
        {
            words = rest;
        }

        return check.Run.Any(entry => StartsWith(words, CommandLine.Words(entry)));
    }

    /// <summary>The words <paramref name="command"/> splits into, at whitespace outside quotes, with the quotes dropped.</summary>
    /// <param name="command">A command line, such as a check's run entry.</param>
    /// <returns>Its words, the program first.</returns>
    public static IReadOnlyList<string> Words(string command) => CommandLine.Words(command);

    /// <summary>
    /// Joins <paramref name="words"/>, such as the program and arguments <c>axm evidence record</c> runs, into one line
    /// that <see cref="Words"/> reads back as the same words. A word with anything but letters, digits and
    /// <c>_ @ % + = : , . / \ ~ -</c> goes in single quotes, so a filter's <c>|</c> or <c>;</c> reads as part of it, never
    /// as a separator, and the line still <see cref="Counts"/>.
    /// </summary>
    /// <param name="words">The words, the program first.</param>
    /// <returns>The line.</returns>
    public static string Join(IEnumerable<string> words) => string.Join(' ', words.Select(Quoted));

    private static string Quoted(string word) =>
        word.Length > 0 && word.All(character => char.IsAsciiLetterOrDigit(character) || "_@%+=:,./\\~-".Contains(character))
            ? word
            // A quote inside goes in double quotes between two single-quoted parts, which read back as one word.
            : $"'{word.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";

    /// <summary>
    /// Whether <paramref name="command"/> may join several commands: with <c>&amp;&amp;</c>, <c>||</c>, <c>;</c>,
    /// <c>|</c>, a line break or a trailing <c>&amp;</c> outside quotes. One exit code can't vouch for one check then: a
    /// pipe's is usually its last command's. A redirection such as <c>2&gt;&amp;1</c> joins nothing.
    /// </summary>
    /// <remarks>
    /// The command may run in bash, where <c>\</c> escapes, or PowerShell, where a backtick does, so it's read three
    /// ways: with no escape, with each, and a separator any reading finds makes it compound. A line break anywhere, or
    /// a quote left open, such as an apostrophe in a comment, also makes it compound. Each of these only ever stops a
    /// command from counting, which is the safe way to be wrong.
    /// </remarks>
    /// <param name="command">A command line.</param>
    /// <returns>Whether it's compound, or may be.</returns>
    public static bool IsCompound(string command)
    {
        var text = command.Trim();
        return text.Contains('\n') || text.Contains('\r') || Joins(text, null) || Joins(text, '\\') || Joins(text, '`');
    }

    private static bool Joins(string text, char? escape)
    {
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if (character == escape && quote != '\'')
            {
                // An escaped separator is safest read as one, and an escaped quote opens and closes nothing.
                if (i + 1 < text.Length && text[i + 1] is ';' or '|' or '&')
                {
                    return true;
                }

                i++;
                continue;
            }

            if (quote is not null)
            {
                quote = character == quote ? null : quote;
                continue;
            }

            switch (character)
            {
                case '"' or '\'':
                    quote = character;
                    break;
                case ';' or '|':
                    return true;
                case '&':
                    var redirection = (i > 0 && text[i - 1] is '>' or '<') || (i + 1 < text.Length && text[i + 1] == '>');
                    var callOperator = i == 0 && text.Length > 1 && char.IsWhiteSpace(text[1]);
                    if (!redirection && !callOperator)
                    {
                        return true;
                    }

                    break;
            }
        }

        return quote is not null;
    }

    // A program on PATH matches by name, so a full path or a .exe counts. An entry that names a script by its path
    // matches only that path, give or take ./ and the slashes, since another test.sh elsewhere is another script.
    private static bool StartsWith(List<string> words, List<string> entry) =>
        entry.Count > 0
        && words.Count >= entry.Count
        && (entry[0].Contains('/') || entry[0].Contains('\\')
            ? ScriptPath(words[0]) == ScriptPath(entry[0])
            : CommandLine.Program(words[0]) == CommandLine.Program(entry[0]))
        && words.Skip(1).Take(entry.Count - 1).SequenceEqual(entry.Skip(1), StringComparer.Ordinal);

    private static string ScriptPath(string word)
    {
        var path = word.Replace('\\', '/');
        path = path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path;
        var folder = path.LastIndexOf('/') + 1;
        return path[..folder] + CommandLine.Program(path[folder..]);
    }
}

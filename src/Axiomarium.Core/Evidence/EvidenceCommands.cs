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

    /// <summary>
    /// Whether <paramref name="command"/> joins several commands, outside quotes: with <c>&amp;&amp;</c>,
    /// <c>||</c>, <c>;</c>, <c>|</c>, a line break or a trailing <c>&amp;</c>. One exit code can't vouch for one check
    /// then: a pipe's is usually its last command's. A redirection such as <c>2&gt;&amp;1</c> joins nothing.
    /// </summary>
    /// <param name="command">A command line.</param>
    /// <returns>Whether it's compound.</returns>
    public static bool IsCompound(string command)
    {
        var text = command.Trim();
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
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
                case ';' or '|' or '\n' or '\r':
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

        return false;
    }

    private static bool StartsWith(List<string> words, List<string> entry) =>
        entry.Count > 0
        && words.Count >= entry.Count
        && CommandLine.Program(words[0]) == CommandLine.Program(entry[0])
        && words.Skip(1).Take(entry.Count - 1).SequenceEqual(entry.Skip(1), StringComparer.Ordinal);
}

using System.Text;

namespace Axiomarium.Core.Evals;

/// <summary>Reads command lines the way a shell splits them into words, closely enough to name what they run.</summary>
internal static class CommandLine
{
    private static readonly string[] Launchers = [".exe", ".cmd", ".bat", ".com"];

    /// <summary>
    /// Splits <paramref name="text"/> into words at whitespace outside quotes, and drops the quotes. Quoted parts
    /// next to each other join into one word, as <c>'a'"b"</c> does in a shell, and a quote of the other kind
    /// inside quotes is kept.
    /// </summary>
    public static List<string> Words(string text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var inWord = false;
        char? quote = null;
        foreach (var character in text)
        {
            if (quote is not null)
            {
                if (character == quote)
                {
                    quote = null;
                }
                else
                {
                    word.Append(character);
                }
            }
            else if (character is '"' or '\'')
            {
                quote = character;
                inWord = true;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (inWord)
                {
                    words.Add(word.ToString());
                    word.Clear();
                    inWord = false;
                }
            }
            else
            {
                word.Append(character);
                inWord = true;
            }
        }

        if (inWord)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    /// <summary>The program <paramref name="word"/> names, without its folder or a <c>.exe</c>, <c>.cmd</c>, <c>.bat</c> or <c>.com</c> extension.</summary>
    public static string Program(string word)
    {
        var name = word[(word.LastIndexOfAny(['/', '\\']) + 1)..];
        var launcher = Launchers.FirstOrDefault(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        return launcher is null ? name : name[..^launcher.Length];
    }
}

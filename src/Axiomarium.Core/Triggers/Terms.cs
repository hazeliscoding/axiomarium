using System.Text.RegularExpressions;

namespace Axiomarium.Core.Triggers;

/// <summary>Splits text into the terms overlap is measured on: lowercase words, without common ones, reduced to a stem.</summary>
internal static partial class Terms
{
    // Function words, and the words every skill description uses to say when it applies.
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "about", "above", "after", "again", "all", "also", "and", "any", "are", "ask", "asks", "because", "been", "before",
        "being", "below", "between", "both", "but", "can", "could", "did", "does", "doing", "done", "down", "during", "each",
        "else", "etc", "even", "every", "few", "first", "for", "from", "further", "get", "gets", "had", "has", "have",
        "having", "her", "here", "him", "his", "how", "into", "its", "just", "like", "make", "makes", "making", "may",
        "might", "more", "most", "must", "need", "needs", "new", "nor", "not", "now", "off", "once", "one", "only", "onto",
        "other", "our", "out", "over", "own", "per", "same", "shall", "she", "should", "skill", "skills", "some", "such",
        "take", "takes", "than", "that", "the", "their", "them", "then", "there", "these", "they", "this", "those",
        "through", "too", "turn", "turns", "under", "until", "upon", "use", "used", "uses", "using", "user", "users",
        "very", "via", "want", "wants", "was", "way", "well", "were", "what", "when", "whenever", "where", "whether",
        "which", "while", "who", "whom", "whose", "why", "will", "with", "within", "without", "would", "yes", "you",
        "your", "yours",
    };

    /// <summary>The terms in <paramref name="text"/>, in order, each as its stem and the word it came from.</summary>
    /// <param name="text">Any text.</param>
    /// <returns>Words of three letters or more that aren't common, lowercase; forms of one word share a stem, such as <c>deploy</c> for deploys, deployed and deploying.</returns>
    public static IEnumerable<(string Stem, string Word)> Of(string text)
    {
        foreach (Match match in Word().Matches(text.ToLowerInvariant()))
        {
            var word = match.Value;
            if (word.Length >= 3 && !Common.Contains(word) && !word.All(char.IsAsciiDigit))
            {
                yield return (Stem(word), word);
            }
        }
    }

    // A few English suffixes, enough that the forms of a word meet. The stem is never shown.
    private static string Stem(string word)
    {
        var stem = word switch
        {
            _ when word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal) => word[..^3] + "y",
            _ when word.Length > 5 && word.EndsWith("ing", StringComparison.Ordinal) => word[..^3],
            _ when word.Length > 4 && word.EndsWith("ed", StringComparison.Ordinal) && !word.EndsWith("eed", StringComparison.Ordinal) => word[..^2],
            _ when word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) => word[..^1],
            _ => word,
        };
        return stem.Length > 4 && stem.EndsWith('e') ? stem[..^1] : stem;
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();
}

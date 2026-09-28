using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace Axiomarium.Core.Paths;

/// <summary>A glob pattern for relative paths with forward slashes.</summary>
/// <remarks>
/// <c>*</c> matches within one folder, <c>**</c> across any number of folders, <c>?</c> one character
/// other than <c>/</c>, and <c>{a,b}</c> either alternative, nested as needed. A pattern that ends with
/// <c>/</c> matches everything under that folder. Everything else is literal, and matching is
/// case-sensitive, so a pattern means the same thing on every platform.
/// </remarks>
public sealed class Glob
{
    private readonly Regex _regex;

    private Glob(string pattern, Regex regex)
    {
        Pattern = pattern;
        _regex = regex;
    }

    /// <summary>The pattern as written.</summary>
    public string Pattern { get; }

    /// <summary>Reads a pattern.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="glob">The glob, when the pattern is valid.</param>
    /// <param name="problem">Why the pattern isn't valid, as a sentence that names the column, when it isn't.</param>
    /// <returns>Whether the pattern is valid: not empty, and every brace closed.</returns>
    public static bool TryParse(string pattern, [NotNullWhen(true)] out Glob? glob, [NotNullWhen(false)] out string? problem)
    {
        glob = null;
        if (pattern.Length == 0)
        {
            problem = "The pattern is empty.";
            return false;
        }

        var source = pattern.EndsWith('/') ? pattern + "**" : pattern;
        var regex = new StringBuilder("^");
        var openBraces = new Stack<int>();
        for (var i = 0; i < source.Length; i++)
        {
            switch (source[i])
            {
                case '*' when i + 1 < source.Length && source[i + 1] == '*':
                    // "**/" also matches no folder at all, so **/*.md matches a.md.
                    var folders = i + 2 < source.Length && source[i + 2] == '/';
                    regex.Append(folders ? "(?:.*/)?" : ".*");
                    i += folders ? 2 : 1;
                    break;
                case '*':
                    regex.Append("[^/]*");
                    break;
                case '?':
                    regex.Append("[^/]");
                    break;
                case '{':
                    openBraces.Push(i);
                    regex.Append("(?:");
                    break;
                case ',' when openBraces.Count > 0:
                    regex.Append('|');
                    break;
                case '}' when openBraces.Count == 0:
                    problem = $"'}}' at column {i + 1} has no '{{' before it.";
                    return false;
                case '}':
                    openBraces.Pop();
                    regex.Append(')');
                    break;
                default:
                    regex.Append(Regex.Escape(source[i].ToString()));
                    break;
            }
        }

        if (openBraces.Count > 0)
        {
            problem = $"'{{' at column {openBraces.Peek() + 1} is never closed.";
            return false;
        }

        glob = new Glob(pattern, new Regex(regex.Append('$').ToString(), RegexOptions.CultureInvariant));
        problem = null;
        return true;
    }

    /// <summary>Whether <paramref name="path"/> matches.</summary>
    /// <param name="path">A path relative to the pattern's base, with forward slashes and no leading slash.</param>
    /// <returns>Whether the whole path matches.</returns>
    public bool IsMatch(string path) => _regex.IsMatch(path);
}

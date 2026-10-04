using System.Text.RegularExpressions;

namespace Axiomarium.GroundTruth;

/// <summary>Fills in where a scenario runs, which its config files can't know in advance.</summary>
internal static partial class RunFolder
{
    /// <summary>
    /// Replaces each <c>{run}</c> path, up to the quote or colon that ends it, with that path under the run folder in
    /// the platform's form. Codex matches a trust key and a project's path only as it writes them, with backslashes on
    /// Windows, so a scenario writes them with <c>{run}</c> and forward slashes, inside single-quoted TOML strings.
    /// </summary>
    /// <param name="text">A config file's text.</param>
    /// <param name="run">The run folder, absolute.</param>
    /// <returns>The text with every <c>{run}</c> path filled in.</returns>
    public static string FillIn(string text, string run) =>
        RunPath().Replace(text, match => (run + match.Groups[1].Value).Replace('/', Path.DirectorySeparatorChar));

    [GeneratedRegex("""\{run\}([^'":]*)""")]
    private static partial Regex RunPath();
}

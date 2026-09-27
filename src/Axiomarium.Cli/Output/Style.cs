namespace Axiomarium.Cli.Output;

/// <summary>How fancy output may be: color, kaomoji, both, or neither.</summary>
/// <param name="Color">Whether to write ANSI colors.</param>
/// <param name="Kaomoji">Whether to add a kaomoji to summary and status lines.</param>
public sealed record Style(bool Color, bool Kaomoji)
{
    /// <summary>No color and no kaomoji: what pipes, CI, hooks and agents read.</summary>
    public static Style Plain { get; } = new(false, false);

    /// <summary>Decides the style for one output stream.</summary>
    /// <param name="redirected">Whether the stream is a file or pipe. Redirected output is always plain.</param>
    /// <param name="environment">The process's environment. <c>AXM_PLAIN</c> turns everything off, <c>NO_COLOR</c> and <c>TERM=dumb</c> turn color off.</param>
    /// <param name="virtualTerminal">Whether the terminal understands ANSI escape codes.</param>
    /// <returns>The style to write with.</returns>
    public static Style For(bool redirected, IReadOnlyDictionary<string, string?> environment, bool virtualTerminal)
    {
        var kaomoji = !redirected && !IsSet(environment, "AXM_PLAIN");
        var color = kaomoji
            && virtualTerminal
            && !IsSet(environment, "NO_COLOR")
            && !(environment.TryGetValue("TERM", out var term) && term == "dumb");
        return new Style(color, kaomoji);
    }

    // NO_COLOR counts only when it is set to something: https://no-color.org
    private static bool IsSet(IReadOnlyDictionary<string, string?> environment, string name) =>
        environment.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value);
}

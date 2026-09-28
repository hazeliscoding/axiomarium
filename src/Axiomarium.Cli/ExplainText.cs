using Axiomarium.Cli.Output;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Cli;

/// <summary>The report <c>axm explain</c> prints: what each harness loads for a file, and what it drops.</summary>
public static class ExplainText
{
    /// <summary>Writes the heading, one block per harness with its loaded files then its dropped ones, and the summary.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="explanation">What each harness loads.</param>
    /// <param name="home">The user's home folder, shown as <c>~</c>.</param>
    /// <param name="style">Whether to add color and a kaomoji. Without either, the text is identical.</param>
    public static void Write(TextWriter output, Explanation explanation, string home, Style style)
    {
        var ink = new Ink(output, style);
        var show = Shower(explanation, home);
        WriteHeading(ink, show(explanation.Target), diff: false, launch: null);

        var loaded = explanation.Harnesses.SelectMany(harness => harness.Resolution.Loaded).ToList();
        var dropped = explanation.Harnesses.SelectMany(harness => harness.Resolution.Dropped).ToList();
        var columns = Columns.For(loaded, dropped, show);
        foreach (var harness in explanation.Harnesses)
        {
            ink.Write("  ").Write(Title(harness.Harness).ToUpperInvariant(), Palette.Dim)
                .Write(" // ", Palette.Dim).Write($"launched at {Launch(explanation, home)}").Line();
            WriteLoaded(ink, harness.Resolution.Loaded, columns, show);
            foreach (var item in harness.Resolution.Dropped)
            {
                var keyword = item.Rule.LeftToModel ? NotLoaded : Dropped;
                ink.Write("  ").Write("--", Palette.Dim).Write("  ").Write(show(item.Path).PadRight(columns.Path), Palette.Path)
                    .Write(keyword.PadRight(columns.Keyword), item.Rule.LeftToModel ? Palette.Dim : Palette.Warning)
                    .Write(item.Rule.Label + ImportedBy(item.Via, show))
                    .Line();
            }

            if (harness.Resolution.Loaded.Count == 0 && harness.Resolution.Dropped.Count == 0)
            {
                ink.Write("  ").Write("--", Palette.Dim).Write("  ").Write("no instruction files", Palette.Dim).Line();
            }

            ink.Line();
        }

        var count = explanation.Harnesses.Count;
        ink.Write($"{count} harness{(count == 1 ? "" : "es")}")
            .Write(" · ", Palette.Dim).Write($"{loaded.Count} loaded", Palette.Ok)
            .Write(" · ", Palette.Dim).Write($"{dropped.Count} not loaded")
            .Kaomoji(Kaomoji.AllClear, Palette.Ok)
            .Line();
    }

    /// <summary>
    /// Writes the files only one of the first two harnesses loads, one block per harness, and the summary.
    /// </summary>
    /// <param name="output">Where to write.</param>
    /// <param name="explanation">What each harness loads. It must hold two harnesses.</param>
    /// <param name="home">The user's home folder, shown as <c>~</c>.</param>
    /// <param name="style">Whether to add color and a kaomoji. Without either, the text is identical.</param>
    /// <exception cref="ArgumentException">The explanation doesn't hold two harnesses.</exception>
    public static void WriteDiff(TextWriter output, Explanation explanation, string home, Style style)
    {
        if (explanation.Harnesses is not [var first, var second])
        {
            throw new ArgumentException("A diff compares two harnesses.", nameof(explanation));
        }

        var ink = new Ink(output, style);
        var show = Shower(explanation, home);
        // Without the launch directory, a diff from --cwd would look like one from the repo root.
        WriteHeading(ink, show(explanation.Target), diff: true, explanation.LaunchedAtRepoRoot ? null : Launch(explanation, home));

        var (onlyFirst, onlySecond) = Explainer.Diff(first.Resolution, second.Resolution);
        var columns = Columns.For([.. onlyFirst, .. onlySecond], [], show);
        foreach (var (harness, only) in new[] { (first.Harness, onlyFirst), (second.Harness, onlySecond) })
        {
            ink.Write("  ").Write($"ONLY {Title(harness).ToUpperInvariant()}", Palette.Dim).Line();
            WriteLoaded(ink, only, columns, show);
            if (only.Count == 0)
            {
                ink.Write("  ").Write("--", Palette.Dim).Write("  ").Write("none", Palette.Dim).Line();
            }

            ink.Line();
        }

        ink.Write($"{onlyFirst.Count} only in {Title(first.Harness)}")
            .Write(" · ", Palette.Dim).Write($"{onlySecond.Count} only in {Title(second.Harness)}")
            .Kaomoji(Kaomoji.AllClear, Palette.Ok)
            .Line();
    }

    /// <summary>The harness's name in prose, such as <c>Claude Code</c>.</summary>
    public static string Title(Harness harness) => harness switch
    {
        Harness.ClaudeCode => "Claude Code",
        Harness.Codex => "Codex",
        _ => throw new ArgumentOutOfRangeException(nameof(harness), harness, null),
    };

    private const string Dropped = "DROPPED";
    private const string NotLoaded = "NOT LOADED";

    private static void WriteHeading(Ink ink, string target, bool diff, string? launch)
    {
        ink.Write("AXM EXPLAIN", Palette.Accent).Write(" // ", Palette.Dim).Write(target, Palette.Path);
        if (diff)
        {
            ink.Write(" // ", Palette.Dim).Write("diff");
        }

        if (launch is not null)
        {
            ink.Write(" // ", Palette.Dim).Write($"launched at {launch}");
        }

        ink.Line();
        ink.Line();
    }

    private static void WriteLoaded(Ink ink, IReadOnlyList<LoadedInstruction> loaded, Columns columns, Func<string, string> show)
    {
        for (var i = 0; i < loaded.Count; i++)
        {
            var item = loaded[i];
            var when = item.Timing == LoadTiming.AtLaunch ? "at launch" : "when the file is read";
            if (item.Cut)
            {
                when += $", cut to {item.Bytes} bytes";
            }

            ink.Write("  ").Write($"{i + 1:00}", Palette.Dim).Write("  ").Write(show(item.Path).PadRight(columns.Path), Palette.Path)
                .Write(LabelOf(item).PadRight(columns.Label), Palette.Bold)
                .Write(when + ImportedBy(item.Via, show), Palette.Dim)
                .Line();
        }
    }

    private static string LabelOf(LoadedInstruction item) =>
        item.Patterns is { Count: > 0 } patterns ? $"paths: {string.Join(", ", patterns)}" : item.Rule.Label;

    private static string ImportedBy(ImportSite? via, Func<string, string> show) =>
        via is null ? "" : $", imported by {show(via.File)}:{via.Line}";

    private static string Launch(Explanation explanation, string home) =>
        explanation.LaunchedAtRepoRoot ? "the repo root" : DisplayPath.Of(explanation.Launch, explanation.RepoRoot, home);

    private static Func<string, string> Shower(Explanation explanation, string home) => path => DisplayPath.Of(path, explanation.RepoRoot, home);

    // Column widths across the whole report, so every block lines up.
    private sealed record Columns(int Path, int Label, int Keyword)
    {
        public static Columns For(IReadOnlyList<LoadedInstruction> loaded, IReadOnlyList<DroppedInstruction> dropped, Func<string, string> show)
        {
            var paths = loaded.Select(item => item.Path).Concat(dropped.Select(item => item.Path)).Select(path => show(path).Length);
            var keywords = dropped.Select(item => item.Rule.LeftToModel ? NotLoaded.Length : Dropped.Length);
            return new Columns(
                paths.DefaultIfEmpty(0).Max() + 3,
                loaded.Select(item => LabelOf(item).Length).DefaultIfEmpty(0).Max() + 3,
                keywords.DefaultIfEmpty(0).Max() + 2);
        }
    }
}

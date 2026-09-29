using System.Globalization;
using Axiomarium.Cli.Output;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Cli;

/// <summary>Renders <c>axm triggers</c>: each harness's overlapping skill pairs, with the terms they share.</summary>
internal static class TriggersText
{
    /// <summary>Writes the report: a block per harness, then the summary line.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="report">The overlap in each harness's listing.</param>
    /// <param name="home">The home folder, shown as <c>~</c>.</param>
    /// <param name="style">Whether to color and add a kaomoji.</param>
    public static void Write(TextWriter output, TriggerReport report, string home, Style style)
    {
        var ink = new Ink(output, style);
        var pairs = report.Harnesses.Sum(harness => harness.Pairs.Count);
        ink.Write("AXM TRIGGERS", Palette.Accent).Write(" // ", Palette.Dim)
            .Write($"{Count(report.Harnesses.Count, "harness", "harnesses")} · {Count(pairs, "overlapping pair")}").Line();
        ink.Line();

        string Show(string path) => DisplayPath.Of(path, report.RepoRoot, home);
        var index = 0;
        foreach (var harness in report.Harnesses)
        {
            var fromVault = harness.Compared.Count(skill => skill.FromVault);
            ink.Write("  ").Write(ExplainText.Title(harness.Harness).ToUpperInvariant(), Palette.Dim).Write(" // ", Palette.Dim)
                .Write(Count(harness.Compared.Count, "skill") + " compared")
                .Write(fromVault > 0 ? $", {fromVault} from the vault" : "")
                .Write(harness.LeftOut > 0 ? $" · {Count(harness.LeftOut, "built-in skill")} left out, their text isn't recorded" : "", Palette.Dim)
                .Write(harness.Repeats > 0 ? $" · {Count(harness.Repeats, "skill")} left out, named like one listed before" : "", Palette.Dim)
                .Line();
            if (harness.Pairs.Count == 0)
            {
                ink.Write("  ").Write("--", Palette.Dim).Write("  ").Write("no overlap", Palette.Dim).Line();
                ink.Line();
                continue;
            }

            // The score leads, because plugin skill names are long enough that aligning after them wastes the line.
            foreach (var pair in harness.Pairs)
            {
                index++;
                ink.Write("  ").Write($"{index:00}", Palette.Dim).Write("  ").Write(pair.Score.ToString("0.00", CultureInfo.InvariantCulture), Palette.Warning)
                    .Write("  ").Write($"{pair.First.Name} ~ {pair.Second.Name}", Palette.Bold)
                    .Write("  shares ", Palette.Dim).Write(string.Join(", ", pair.Shared)).Line();
                ink.Write("      ").Write(Show(pair.First.Path), Palette.Path).Line();
                ink.Write("      ").Write(Show(pair.Second.Path), Palette.Path).Line();
            }

            ink.Line();
        }

        // Overlap isn't a finding: only running prompts past the model can show that it picks the wrong skill.
        ink.Write(Count(pairs, "overlapping pair"));
        ink.Write(pairs > 0 ? ". Shared wording, not proof the agent mixes them up." : "", Palette.Dim);
        ink.Kaomoji(Kaomoji.ForOutcome(0, pairs), pairs > 0 ? Palette.Warning : Palette.Ok).Line();
    }

    private static string Count(int count, string noun, string? plural = null) => $"{count} {(count == 1 ? noun : plural ?? noun + "s")}";
}

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

    /// <summary>Writes the prompts <c>axm triggers generate</c> got, grouped by kind, and says a model wrote them.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="file">The prompts, labeled with the model that wrote them.</param>
    /// <param name="version">The Claude Code version that ran the model, or <see langword="null"/> when it didn't say.</param>
    /// <param name="style">Whether to color.</param>
    public static void WriteGenerated(TextWriter output, TriggerPromptFile file, string? version, Style style)
    {
        var ink = new Ink(output, style);
        var source = file.Generated is { } generated ? $" from {generated.Model} in Claude Code{(version is null ? "" : $" {version}")}" : "";
        ink.Write("AXM TRIGGERS GENERATE", Palette.Accent).Write(" // ", Palette.Dim).Write(file.Skill).Write(" // ", Palette.Dim)
            .Write($"{Count(file.Prompts.Count, "prompt")}{source}").Line();
        ink.Line();

        var index = 0;
        foreach (var group in file.Prompts.GroupBy(prompt => prompt.Kind).OrderBy(group => group.Key))
        {
            var about = group.Key switch
            {
                PromptKind.Positive or PromptKind.Paraphrased => $"should pick {file.Skill}",
                PromptKind.Negative or PromptKind.Adversarial => $"shouldn't pick {file.Skill}",
                _ => $"between {file.Skill} and a rival",
            };
            ink.Write("  ").Write(TriggerPrompts.Name(group.Key).ToUpperInvariant(), Palette.Dim).Write(" // ", Palette.Dim).Write(about).Line();
            foreach (var prompt in group)
            {
                index++;
                ink.Write("  ").Write($"{index:00}", Palette.Dim).Write("  ").Write(prompt.Prompt);
                if (prompt.Kind == PromptKind.Ambiguous)
                {
                    ink.Write($"  picks {(prompt.ShouldTrigger ? file.Skill : prompt.Rival)}", Palette.Dim);
                }

                ink.Line();
            }

            ink.Line();
        }

        ink.Write("A model wrote these prompts. Review them before you rely on them.").Line();
    }

    private static string Count(int count, string noun, string? plural = null) => $"{count} {(count == 1 ? noun : plural ?? noun + "s")}";
}

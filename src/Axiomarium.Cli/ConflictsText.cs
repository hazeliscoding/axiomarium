using Axiomarium.Cli.Output;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Judging;

namespace Axiomarium.Cli;

/// <summary>Renders <c>axm conflicts --judge</c>: each harness's instruction files and the contradictions the judge quoted from them.</summary>
internal static class ConflictsText
{
    /// <summary>Writes the report.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="target">The file, as shown.</param>
    /// <param name="judge">The harness whose model judged.</param>
    /// <param name="model">The model that judged, where known.</param>
    /// <param name="groups">The harnesses that load the same files, those files, and the judge's answer about them.</param>
    /// <param name="style">Whether to color and add a kaomoji.</param>
    public static void Write(
        TextWriter output,
        string target,
        Harness judge,
        string? model,
        IReadOnlyList<(List<Harness> Harnesses, IReadOnlyList<JudgedFile> Files, ConflictAnswer? Answer)> groups,
        Style style)
    {
        var ink = new Ink(output, style);
        ink.Write("AXM CONFLICTS", Palette.Accent).Write(" // ", Palette.Dim)
            .Write($"{target} · judged by {ExplainText.Title(judge)}{(model is null ? "" : $" on {model}")} · model judgment").Line();
        ink.Line();

        var index = 0;
        foreach (var (harnesses, files, answer) in groups)
        {
            var title = string.Join(" + ", harnesses.Select(harness => ExplainText.Title(harness).ToUpperInvariant()));
            if (files.Count == 0)
            {
                ink.Write("  ").Write(title, Palette.Dim).Write(" // ", Palette.Dim).Write("loads no instruction files for this file").Line();
                ink.Line();
                continue;
            }

            ink.Write("  ").Write(title, Palette.Dim).Write(" // ", Palette.Dim).Write($"{files.Count} instruction file{(files.Count == 1 ? "" : "s")}").Line();
            foreach (var contradiction in answer!.Found)
            {
                index++;
                ink.Write("  ").Write($"{index:00}", Palette.Dim).Write("  ").Write($"{contradiction.First.File}:{contradiction.First.Line}", Palette.Path)
                    .Write($"  \"{contradiction.First.Text}\"").Line();
                ink.Write("      ").Write($"{contradiction.Second.File}:{contradiction.Second.Line}", Palette.Path).Write($"  \"{contradiction.Second.Text}\"").Line();
                ink.Write($"      why, in the model's words: {contradiction.Why}", Palette.Dim).Line();
            }

            if (answer.Dropped > 0)
            {
                ink.Write($"  {answer.Dropped} more {(answer.Dropped == 1 ? "was" : "were")} dropped: {(answer.Dropped == 1 ? "its" : "their")} quotes aren't in the files.", Palette.Dim).Line();
            }

            ink.Line();
        }

        var found = groups.Sum(group => group.Answer?.Found.Count ?? 0);
        ink.Write($"{found} contradiction{(found == 1 ? "" : "s")} · the model's judgment, so a rerun can differ")
            .Kaomoji(Kaomoji.ForOutcome(0, found), found > 0 ? Palette.Warning : Palette.Ok).Line();
    }
}

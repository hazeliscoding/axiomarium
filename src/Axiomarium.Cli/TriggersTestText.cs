using System.Globalization;
using System.Text.RegularExpressions;
using Axiomarium.Cli.Output;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Cli;

/// <summary>Renders <c>axm triggers test</c>: the plan, then each harness's scores and problem runs with their causes.</summary>
internal static partial class TriggersText
{
    /// <summary>Writes the plan line, before the sessions run, so a long run says what it's doing.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="plan">The test plan.</param>
    /// <param name="sessions">The sessions that will run, on the harnesses that are installed.</param>
    /// <param name="style">Whether to color.</param>
    public static void WriteTestPlan(TextWriter output, TriggerTestPlan plan, IReadOnlyList<TriggerSession> sessions, Style style)
    {
        var ink = new Ink(output, style);
        var runs = sessions.Max(session => session.Run);
        var harnesses = sessions.Select(session => session.Harness).Distinct().Count();
        ink.Write("AXM TRIGGERS TEST", Palette.Accent).Write(" // ", Palette.Dim)
            .Write($"{Count(plan.Prompts.Count, "skill")} · {Count(plan.Prompts.Values.Sum(file => file.Prompts.Count), "prompt")} × {Count(runs, "run")} · ")
            .Write($"{Count(harnesses, "harness", "harnesses")} · {Count(sessions.Count, "session")}, {TriggerSessions.Parallel} at a time").Line();
        foreach (var (skill, file) in plan.Prompts.Where(skill => skill.Value.Generated is not null))
        {
            ink.Write($"  {skill}'s prompts were written by {file.Generated!.Model} on {file.Generated.Date}", Palette.Dim).Line();
        }

        ink.Line();
    }

    /// <summary>Writes each harness's block, the failed sessions and the summary line.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="results">The scores, with each problem's cause.</param>
    /// <param name="sessions">Every session's result, for the models the harnesses reported.</param>
    /// <param name="versions">The version each installed harness reported, by harness.</param>
    /// <param name="missing">Why each harness that couldn't run was skipped.</param>
    /// <param name="model">The model asked for, or <see langword="null"/> for each harness's own.</param>
    /// <param name="notes">A note about each harness's listing, such as being over its budget, by harness.</param>
    /// <param name="style">Whether to color and add a kaomoji.</param>
    public static void WriteTestResults(
        TextWriter output, TriggerResults results, IReadOnlyList<SessionResult> sessions, IReadOnlyDictionary<Harness, string> versions,
        IReadOnlyDictionary<Harness, string> missing, string? model, IReadOnlyDictionary<Harness, string> notes, Style style)
    {
        var ink = new Ink(output, style);
        var index = 0;
        foreach (var harness in versions.Keys.Concat(missing.Keys).Distinct().Order())
        {
            var title = ExplainText.Title(harness).ToUpperInvariant();
            if (missing.TryGetValue(harness, out var reason))
            {
                ink.Write("  ").Write(title, Palette.Dim).Write(" // ", Palette.Dim).Write($"skipped: {reason}", Palette.Warning).Line();
                ink.Line();
                continue;
            }

            var picker = sessions.FirstOrDefault(session => session.Session.Harness == harness && session.Model is not null)?.Model ?? model ?? "its configured model";
            ink.Write("  ").Write($"{title} {Version().Match(versions[harness]).Value}".TrimEnd(), Palette.Dim).Write(" // ", Palette.Dim).Write($"picks by {picker}").Line();
            if (notes.TryGetValue(harness, out var note))
            {
                ink.Write($"  note: {note}", Palette.Dim).Line();
            }

            foreach (var skill in results.Background.Where(skill => skill.Harness == harness))
            {
                ink.Write($"  background: {skill.Name}, loaded in {skill.Runs} of {Count(skill.Of, "run")}, so it isn't counted as a pick", Palette.Dim).Line();
            }

            var scores = results.Scores.Where(score => score.Harness == harness).ToList();
            var nameWidth = scores.Count == 0 ? 0 : scores.Max(score => score.Skill.Length) + 3;
            foreach (var score in scores)
            {
                index++;
                ink.Write("  ").Write($"{index:00}", Palette.Dim).Write("  ").Write(score.Skill.PadRight(nameWidth), Palette.Bold)
                    .Write(Rate("precision", score.Precision, score.TruePositives, score.TruePositives + score.FalsePositives).PadRight(28))
                    .Write(Rate("recall", score.Recall, score.TruePositives, score.TruePositives + score.FalseNegatives)).Line();
            }

            foreach (var kind in results.Problems.Where(problem => problem.Harness == harness).GroupBy(problem => problem.Kind).OrderBy(kind => kind.Key))
            {
                ink.Line();
                ink.Write("  ").Write(kind.Key switch { ProblemKind.Collision => "COLLISIONS", ProblemKind.FalseTrigger => "FALSE TRIGGERS", _ => "MISSES" }, Palette.Dim).Line();
                foreach (var problem in kind)
                {
                    var runs = $"in {problem.Count} of {Count(problem.Runs, "run")}";
                    var outcome = problem.Kind switch
                    {
                        ProblemKind.Collision => $"picked {problem.Picked} {runs}, expected {problem.Expected}",
                        ProblemKind.FalseTrigger => $"picked {problem.Picked} {runs}, where it shouldn't fire",
                        _ => $"picked no skill {runs}, expected {problem.Expected}",
                    };
                    ink.Write("  ").Write("--", Palette.Dim).Write("  ").Write($"\"{problem.Text.Prompt}\"").Write("  ").Write(outcome, Palette.Warning).Line();
                    ink.Write("      ").Write(problem.Cause ?? "", Palette.Dim).Line();
                }
            }

            ink.Line();
        }

        if (results.Failed.Count > 0)
        {
            ink.Write("  ").Write("FAILED", Palette.Dim).Write(" // ", Palette.Dim).Write($"{Count(results.Failed.Count, "session")} gave no answer and aren't scored").Line();
            foreach (var group in results.Failed.GroupBy(result => (result.Session.Harness, result.Problem)).OrderBy(group => group.Key.Harness))
            {
                ink.Write("  ").Write("--", Palette.Dim).Write("  ").Write($"{group.Key.Harness.Name()}  {Count(group.Count(), "session")}: {group.Key.Problem}").Line();
            }

            ink.Line();
        }

        var scoredRuns = sessions.Count - results.Failed.Count;
        var problemRuns = results.Problems.Sum(problem => problem.Count);
        ink.Write("Picks are made by each harness's model, so a rerun can differ.", Palette.Dim).Line();
        ink.Write($"{Count(scoredRuns, "run")} scored · {problemRuns} with a problem · {results.Failed.Count} failed")
            .Kaomoji(Output.Kaomoji.ForOutcome(0, problemRuns + results.Failed.Count), problemRuns + results.Failed.Count > 0 ? Palette.Warning : Palette.Ok).Line();
    }

    // A rate with the counts it comes from, or n/a when there's nothing to divide by.
    private static string Rate(string name, double? rate, int part, int whole) =>
        rate is { } value ? $"{name} {value.ToString("0.00", CultureInfo.InvariantCulture)} ({part} of {whole})" : $"{name} n/a";

    [GeneratedRegex(@"\d+\.\d+\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex Version();
}

using System.Globalization;
using System.Text.RegularExpressions;
using Axiomarium.Cli.Output;
using Axiomarium.Core.Evals;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Cli;

/// <summary>Renders <c>axm eval compare</c>: the plan, then each case on each harness with its baseline and candidate side by side.</summary>
internal static partial class EvalCompareText
{
    private const int Label = 18;
    private const int Column = 24;

    /// <summary>Writes the plan, before the sessions run.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="asset">The asset's folder.</param>
    /// <param name="gitRef">The baseline's ref, or <c>none</c>.</param>
    /// <param name="specs">The case runs, once for each version.</param>
    /// <param name="sessions">How many sessions will run.</param>
    /// <param name="reused">The saved run the baseline reuses, or <see langword="null"/>.</param>
    /// <param name="repoRoot">The repo root, which shown paths start from.</param>
    /// <param name="casesChanged">Whether the cases differ from the ref's.</param>
    /// <param name="notes">What was left out, and why.</param>
    /// <param name="style">Whether to color.</param>
    public static void WritePlan(
        TextWriter output, string asset, string gitRef, IReadOnlyList<EvalSessionSpec> specs, int sessions, SavedRun? reused, string repoRoot,
        bool casesChanged, IReadOnlyList<string> notes, Style style)
    {
        var ink = new Ink(output, style);
        var cases = specs.Select(item => (item.Case.Type, item.Case.Name)).Distinct().Count();
        var harnesses = specs.Select(item => item.Harness).Distinct().Count();
        ink.Write("AXM EVAL COMPARE", Palette.Accent).Write(" // ", Palette.Dim)
            .Write($"{asset} · {(gitRef == "none" ? "no asset" : gitRef)} against the working tree · {Count(cases, "case")} × {Count(specs.Max(item => item.Run), "run")} · ")
            .Write($"{Count(harnesses, "harness", "harnesses")} · {Count(sessions, "session")}, {EvalSessions.Parallel} at a time").Line();
        ink.Write("  Baseline and candidate sessions alternate, each in a sealed home with your logins and model, and nothing else of your setup.", Palette.Dim).Line();
        if (reused is not null)
        {
            var shown = Path.GetRelativePath(repoRoot, reused.File).Replace(Path.DirectorySeparatorChar, '/');
            ink.Write($"  The baseline reuses the run saved {reused.Date.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC in {shown}.", Palette.Dim).Line();
        }

        if (casesChanged)
        {
            ink.Write($"  The cases changed since {gitRef} too, and both versions ran the working tree's.", Palette.Dim).Line();
        }

        foreach (var note in notes)
        {
            ink.Write($"  note: {note}", Palette.Dim).Line();
        }

        ink.Line();
    }

    /// <summary>Writes each harness's cases side by side, where the history went, and the summary line.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="report">The compare.</param>
    /// <param name="warmup">The Codex warm-up, when there was one.</param>
    /// <param name="saved">The history files saved, as shown paths.</param>
    /// <param name="style">Whether to color and add a kaomoji.</param>
    public static void WriteResults(TextWriter output, CompareReport report, EvalWarmup? warmup, IReadOnlyList<string> saved, Style style)
    {
        var ink = new Ink(output, style);
        if (warmup is not null)
        {
            ink.Write("  ").Write("WARM-UP", Palette.Dim).Write(" // ", Palette.Dim)
                .Write($"1 unscored Codex session to start its sandbox, {Time(warmup.Elapsed.TotalSeconds)}{(warmup.Tokens is { } tokens ? $", {Number(tokens.Input)} input tokens" : "")}")
                .Write(warmup.Problem is null ? "" : $" // {warmup.Problem}", Palette.Warning).Line();
            ink.Line();
        }

        var index = 0;
        foreach (var harness in report.Harnesses.Select(item => item.Harness).Concat(report.Skipped.Keys).Distinct().Order())
        {
            var title = ExplainText.Title(harness).ToUpperInvariant();
            if (report.Skipped.TryGetValue(harness, out var reason))
            {
                ink.Write("  ").Write(title, Palette.Dim).Write(" // ", Palette.Dim).Write($"skipped: {reason}", Palette.Warning).Line();
                ink.Line();
                continue;
            }

            var ran = report.Harnesses.First(item => item.Harness == harness);
            ink.Write("  ").Write($"{title} {Version().Match(ran.Version).Value}".TrimEnd(), Palette.Dim).Write(" // ", Palette.Dim).Write(ran.Model ?? "its default model").Line();
            foreach (var compared in report.Cases.Where(item => item.Harness == harness))
            {
                index++;
                var name = compared.Case.Name + (compared.Case.Type == EvalType.Regression ? " (regression)" : "");
                var (before, after) = (compared.Baseline, compared.Candidate);
                var rows = new List<(string Label, string Before, string After, string Change)>
                {
                    ("passed", $"{before.Passed} of {before.Runs}", $"{after.Passed} of {after.Runs}", Signed(after.Passed - before.Passed, value => value.ToString(CultureInfo.InvariantCulture))),
                };
                Measure(rows, "input tokens", before.Input, after.Input, Number);
                Measure(rows, "cached tokens", before.Cached, after.Cached, Number);
                Measure(rows, "output tokens", before.Output, after.Output, Number);
                Measure(rows, "time", before.Seconds, after.Seconds, Time);
                Measure(rows, "tool calls", before.ToolCalls, after.ToolCalls, Number);
                Measure(rows, "turns", before.Turns, after.Turns, Number);
                Measure(rows, "cost", before.Cost, after.Cost, Dollars);
                if (before.Judge is not null || after.Judge is not null)
                {
                    rows.Add(("judge", Judged(before.Judge), Judged(after.Judge), "model judgment"));
                }

                // A median with its range can outgrow a column, so each table's columns fit its widest value.
                var width = Math.Max(Column, rows.Max(row => Math.Max(row.Before.Length, row.After.Length)) + 2);
                ink.Write("  ").Write($"{index:00}", Palette.Dim).Write("  ").Write(name.PadRight(Label + 2), Palette.Bold)
                    .Write("baseline".PadRight(width), Palette.Dim).Write("candidate".PadRight(width), Palette.Dim).Write("change", Palette.Dim).Line();
                foreach (var (label, was, now, change) in rows)
                {
                    ink.Write($"      {label.PadRight(Label)}", Palette.Dim).Write(was.PadRight(width)).Write(now.PadRight(width)).Write(change, Palette.Dim).Line();
                }
            }

            ink.Line();
        }

        ink.Write($"Each run is the harness's model at work, so a rerun can differ, and {Count(report.Runs, "run")} a side is a small sample.", Palette.Dim).Line();
        foreach (var file in saved)
        {
            ink.Write("Saved to ", Palette.Dim).Write(file, Palette.Path).Line();
        }

        var (baselinePassed, baselineRuns) = (report.Cases.Sum(item => item.Baseline.Passed), report.Cases.Sum(item => item.Baseline.Runs));
        var (candidatePassed, candidateRuns) = (report.Cases.Sum(item => item.Candidate.Passed), report.Cases.Sum(item => item.Candidate.Runs));
        ink.Write($"baseline passed {baselinePassed} of {Count(baselineRuns, "run")} · candidate passed {candidatePassed} of {candidateRuns}")
            .Kaomoji(Kaomoji.ForOutcome(0, candidateRuns - candidatePassed), candidatePassed == candidateRuns ? Palette.Ok : Palette.Warning).Line();
    }

    private static void Measure(List<(string, string, string, string)> rows, string label, Spread? before, Spread? after, Func<double, string> format)
    {
        if (before is null && after is null)
        {
            return;
        }

        rows.Add((
            label,
            before is null ? "n/a" : Shown(before, format),
            after is null ? "n/a" : Shown(after, format),
            before is null || after is null ? "" : Signed(after.Median - before.Median, value => format(Math.Abs(value)))));
    }

    // The median, and the range when the runs differ.
    private static string Shown(Spread spread, Func<double, string> format) =>
        spread.Min == spread.Max ? format(spread.Median) : $"{format(spread.Median)} ({format(spread.Min)}–{format(spread.Max)})";

    private static string Signed(double change, Func<double, string> format) =>
        Math.Abs(change) < 0.005 ? "0" : (change > 0 ? "+" : "-") + format(Math.Abs(change));

    private static string Judged((int Passed, int Judged)? judge) => judge is { } value ? $"{value.Passed} of {value.Judged}" : "n/a";

    private static string Number(double value) => Math.Round(value).ToString("N0", CultureInfo.InvariantCulture);

    private static string Dollars(double value) => "$" + value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Time(double seconds) => seconds < 90
        ? $"{Math.Round(seconds).ToString(CultureInfo.InvariantCulture)} s"
        : $"{(int)(seconds / 60)}m {(int)Math.Round(seconds % 60):00}s";

    private static string Count(int count, string noun, string? plural = null) => $"{count} {(count == 1 ? noun : plural ?? noun + "s")}";

    [GeneratedRegex(@"\d+\.\d+\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex Version();
}

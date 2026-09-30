using System.Globalization;
using System.Text.RegularExpressions;
using Axiomarium.Cli.Output;
using Axiomarium.Core.Evals;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Cli;

/// <summary>Renders <c>axm eval run</c>: the plan, then each harness's cases with their counts, and the summary line.</summary>
internal static partial class EvalText
{
    /// <summary>Writes the plan line, before the sessions run, so a long run says what it's doing.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="sessions">The sessions that will run, on the harnesses that are installed.</param>
    /// <param name="notes">Assets left out, and why.</param>
    /// <param name="style">Whether to color.</param>
    public static void WritePlan(TextWriter output, IReadOnlyList<EvalSessionSpec> sessions, IReadOnlyList<string> notes, Style style)
    {
        var ink = new Ink(output, style);
        var assets = sessions.Select(session => session.Asset.Folder).Distinct().Count();
        var cases = sessions.Select(session => (session.Asset.Folder, session.Case.Type, session.Case.Name)).Distinct().Count();
        var harnesses = sessions.Select(session => session.Harness).Distinct().Count();
        ink.Write("AXM EVAL RUN", Palette.Accent).Write(" // ", Palette.Dim)
            .Write($"{Count(assets, "asset")} · {Count(cases, "case")} × {Count(sessions.Max(session => session.Run), "run")} · ")
            .Write($"{Count(harnesses, "harness", "harnesses")} · {Count(sessions.Count, "session")}, {EvalSessions.Parallel} at a time").Line();
        ink.Write("  Each session runs in a sealed home with your logins and model, and nothing else of your setup.", Palette.Dim).Line();
        foreach (var note in notes)
        {
            ink.Write($"  note: {note}", Palette.Dim).Line();
        }

        ink.Line();
    }

    /// <summary>Writes each harness's block, where the history went, and the summary line.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="report">The run.</param>
    /// <param name="saved">The history files the run saved, as shown paths.</param>
    /// <param name="style">Whether to color and add a kaomoji.</param>
    public static void WriteResults(TextWriter output, EvalReport report, IReadOnlyList<string> saved, Style style)
    {
        var ink = new Ink(output, style);
        if (report.Leftovers.Count > 0)
        {
            ink.Write($"  removed {Count(report.Leftovers.Count, "folder")} a stopped run left behind", Palette.Dim).Line();
        }

        if (report.Warmup is { } warmup)
        {
            ink.Write("  ").Write("WARM-UP", Palette.Dim).Write(" // ", Palette.Dim)
                .Write($"1 unscored Codex session to start its sandbox, {Time(warmup.Elapsed.TotalSeconds)}{(warmup.Tokens is { } tokens ? $", {Number(tokens.Input)} input tokens" : "")}")
                .Write(warmup.Problem is null ? "" : $" // {warmup.Problem}", Palette.Warning).Line();
            ink.Line();
        }

        var summaries = EvalSummaries.Summarize(report.Results);
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
            ink.Write("  ").Write($"{title} {Version().Match(ran.Version).Value}".TrimEnd(), Palette.Dim).Write(" // ", Palette.Dim)
                .Write(ran.Model ?? "its default model").Line();
            foreach (var summary in summaries.Where(summary => summary.Harness == harness))
            {
                index++;
                var label = $"{summary.Asset.Name} // {summary.Case.Name}{(summary.Case.Type == EvalType.Regression ? " (regression)" : "")}";
                ink.Write("  ").Write($"{index:00}", Palette.Dim).Write("  ").Write(label.PadRight(44), Palette.Bold)
                    .Write($"passed in {summary.Passed} of {Count(summary.Runs, "run")}", summary.Passed == summary.Runs ? Palette.Ok : Palette.Warning).Line();
                var tokens = summary.Input is null
                    ? "no token counts"
                    : $"{Measure(summary.Input, Number)} in, {Measure(summary.Cached!, Number)} cached · {Measure(summary.Output!, Number)} out";
                ink.Write($"      tokens  {tokens}", Palette.Dim).Line();
                var work = new List<string> { Measure(summary.Seconds, Time) };
                if (summary.ToolCalls is not null)
                {
                    work.Add($"{Measure(summary.ToolCalls, Number)} tool calls");
                }

                if (summary.Turns is not null)
                {
                    work.Add($"{Measure(summary.Turns, Number)} turns");
                }

                if (summary.Cost is not null)
                {
                    work.Add(Measure(summary.Cost, Dollars));
                }

                ink.Write($"      work    {string.Join(" · ", work)}", Palette.Dim).Line();
                if (summary.Slowest is { } slowest)
                {
                    ink.Write($"      slowest {Time(slowest.Call.Seconds)} in run {slowest.Run}: {Shortened(slowest.Call.What)}", Palette.Dim).Line();
                }
                foreach (var failure in summary.Failures)
                {
                    ink.Write("      ").Write("--", Palette.Dim).Write("  ").Write(EvalChecks.Describe(failure.Check))
                        .Write(" // ", Palette.Dim).Write($"failed in {Runs(failure.Runs)}: {failure.Observed}", Palette.Warning).Line();
                }

                foreach (var (run, stop) in summary.Stops)
                {
                    ink.Write("      ").Write("--", Palette.Dim).Write("  ").Write($"run {run} stopped: {stop}", Palette.Warning).Line();
                }

                foreach (var (run, paths) in summary.Outside)
                {
                    var shown = string.Join(", ", paths.Take(3)) + (paths.Count > 3 ? $" and {paths.Count - 3} more" : "");
                    ink.Write("      ").Write("--", Palette.Dim).Write("  ").Write($"run {run} read outside its copy: {shown}", Palette.Warning).Line();
                }
            }

            ink.Line();
        }

        var failed = report.Results.Count(result => !result.Passed);
        ink.Write("Each run is the harness's model at work, so a rerun can differ.", Palette.Dim).Line();
        foreach (var file in saved)
        {
            ink.Write("Saved to ", Palette.Dim).Write(file, Palette.Path).Line();
        }

        ink.Write($"{Count(report.Results.Count, "run")} · {report.Results.Count - failed} passed · {failed} failed")
            .Kaomoji(Kaomoji.ForOutcome(0, failed), failed > 0 ? Palette.Warning : Palette.Ok).Line();
    }

    // The median, and the range when the runs differ.
    private static string Measure(Spread spread, Func<double, string> format) =>
        spread.Min == spread.Max ? format(spread.Median) : $"{format(spread.Median)} ({format(spread.Min)}–{format(spread.Max)})";

    private static string Shortened(string text) => text.Length <= 90 ? text : text[..89] + "…";

    private static string Number(double value) => Math.Round(value).ToString("N0", CultureInfo.InvariantCulture);

    private static string Dollars(double value) => "$" + value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Time(double seconds) => seconds < 90
        ? $"{Math.Round(seconds).ToString(CultureInfo.InvariantCulture)} s"
        : $"{(int)(seconds / 60)}m {(int)Math.Round(seconds % 60):00}s";

    private static string Runs(IReadOnlyList<int> runs) =>
        runs.Count == 1 ? $"run {runs[0]}" : $"runs {string.Join(", ", runs.Take(runs.Count - 1))} and {runs[^1]}";

    private static string Count(int count, string noun, string? plural = null) => $"{count} {(count == 1 ? noun : plural ?? noun + "s")}";

    [GeneratedRegex(@"\d+\.\d+\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex Version();
}

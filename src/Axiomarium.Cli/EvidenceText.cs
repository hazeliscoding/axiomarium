using System.Globalization;
using Axiomarium.Cli.Output;
using Axiomarium.Core.Evidence;

namespace Axiomarium.Cli;

/// <summary>Renders <c>axm evidence</c>, <c>axm evidence check</c> and what <c>axm evidence record</c> recorded.</summary>
internal static class EvidenceText
{
    /// <summary>Writes every check's status.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="title">The heading, such as <c>AXM EVIDENCE</c>.</param>
    /// <param name="declared">Every check the repo declares, whose order numbers them.</param>
    /// <param name="statuses">The statuses to show.</param>
    /// <param name="zone">The time zone run times are shown in.</param>
    /// <param name="style">Whether to color and add a kaomoji.</param>
    public static void Write(TextWriter output, string title, IReadOnlyList<EvidenceCheck> declared, IReadOnlyList<EvidenceStatus> statuses, TimeZoneInfo zone, Style style)
    {
        var ink = new Ink(output, style);
        Heading(ink, title, statuses);
        Lines(ink, declared, statuses, zone);
        ink.Line();
        Summary(ink, statuses);
    }

    /// <summary>Writes only the checks that aren't fresh, then the counts of all of them.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="declared">Every check the repo declares, whose order numbers them.</param>
    /// <param name="statuses">The statuses of the checks asked about.</param>
    /// <param name="zone">The time zone run times are shown in.</param>
    /// <param name="style">Whether to color and add a kaomoji.</param>
    public static void WriteCheck(TextWriter output, IReadOnlyList<EvidenceCheck> declared, IReadOnlyList<EvidenceStatus> statuses, TimeZoneInfo zone, Style style)
    {
        var ink = new Ink(output, style);
        Heading(ink, "AXM EVIDENCE CHECK", statuses);
        var notFresh = statuses.Where(status => status.State != EvidenceState.Fresh).ToList();
        if (notFresh.Count > 0)
        {
            Lines(ink, declared, notFresh, zone);
            ink.Line();
        }

        Summary(ink, statuses);
    }

    /// <summary>Writes what a run of <c>axm evidence record</c> recorded.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="record">The record.</param>
    /// <param name="style">Whether to color and add a kaomoji.</param>
    public static void WriteRecorded(TextWriter output, EvidenceRecord record, Style style)
    {
        var ink = new Ink(output, style);
        ink.Write("AXM EVIDENCE RECORD", Palette.Accent).Write(" // ", Palette.Dim).Write($"{record.Check} ")
            .Write(record.Passed ? "passed" : "failed", record.Passed ? Palette.Ok : Palette.Error).Write(" · ", Palette.Dim).Write(Ran(record)).Line();
        ink.Line();
        if (!record.Passed)
        {
            ink.Write(record.Exit is { } exit ? $"exit {exit}" : "no exit code", Palette.Error).Write(" · ", Palette.Dim);
        }

        ink.Write($"recorded in .axm/evidence/{record.Check}.json").Kaomoji(Kaomoji.ForOutcome(record.Passed ? 0 : 1, 0), record.Passed ? Palette.Ok : Palette.Error).Line();
    }

    private static void Heading(Ink ink, string title, IReadOnlyList<EvidenceStatus> statuses)
    {
        ink.Write(title, Palette.Accent).Write(" // ", Palette.Dim).Write(Count(statuses.Count, "check")).Line();
        ink.Line();
    }

    // One line for each status, numbered by the check's place in axiomarium.yaml, and a second for a reason a run doesn't show.
    private static void Lines(Ink ink, IReadOnlyList<EvidenceCheck> declared, IReadOnlyList<EvidenceStatus> statuses, TimeZoneInfo zone)
    {
        var nameWidth = statuses.Max(status => status.Check.Name.Length);
        var stateWidth = statuses.Max(status => Word(status.State).Length);
        var indent = new string(' ', 2 + 2 + 2 + nameWidth + 2 + stateWidth + 2);
        foreach (var status in statuses)
        {
            var number = declared.ToList().FindIndex(check => check.Name == status.Check.Name) + 1;
            ink.Write("  ").Write($"{number:00}", Palette.Dim).Write("  ").Write(status.Check.Name.PadRight(nameWidth)).Write("  ")
                .Write(Word(status.State).PadRight(stateWidth), Color(status.State)).Write("  ");
            if (status.Record is not { } record)
            {
                ink.Write(status.Reason!).Line();
                continue;
            }

            ink.Write($"{(record.Passed ? "passed" : "failed")} {When(record.Ended, zone)}").Write(" · ", Palette.Dim).Write(Ran(record)).Line();
            if (status.Reason is { } reason)
            {
                ink.Write(indent).Write(reason, Color(status.State)).Line();
            }
        }
    }

    // The count of checks, then of each state that has any, in the order they're named, and the outcome's kaomoji.
    private static void Summary(Ink ink, IReadOnlyList<EvidenceStatus> statuses)
    {
        ink.Write(Count(statuses.Count, "check"));
        foreach (var state in new[] { EvidenceState.Fresh, EvidenceState.Stale, EvidenceState.Failed, EvidenceState.Missing })
        {
            var count = statuses.Count(status => status.State == state);
            if (count > 0)
            {
                ink.Write(" · ", Palette.Dim).Write($"{count} {Word(state).ToLowerInvariant()}", Color(state));
            }
        }

        var failed = statuses.Count(status => status.State == EvidenceState.Failed);
        var notFresh = statuses.Count(status => status.State is EvidenceState.Stale or EvidenceState.Missing);
        ink.Kaomoji(Kaomoji.ForOutcome(failed, notFresh), failed > 0 ? Palette.Error : notFresh > 0 ? Palette.Warning : Palette.Ok).Line();
    }

    // What ran, with the folder below the repo root it ran in, which may have narrowed it.
    private static string Ran(EvidenceRecord record) => record.Folder == "." ? record.Command : $"{record.Command} in {record.Folder}";

    private static string When(DateTimeOffset moment, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(moment, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Word(EvidenceState state) => state.ToString().ToUpperInvariant();

    private static string Color(EvidenceState state) => state switch
    {
        EvidenceState.Fresh => Palette.Ok,
        EvidenceState.Failed => Palette.Error,
        _ => Palette.Warning,
    };

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}

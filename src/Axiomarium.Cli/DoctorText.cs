using Axiomarium.Cli.Output;
using Axiomarium.Core.Assets;
using Axiomarium.Core.Health;

namespace Axiomarium.Cli;

/// <summary>The report <c>axm doctor</c> prints.</summary>
public static class DoctorText
{
    /// <summary>Writes the heading, one block per kind with a row per asset, each diagnostic, and the summary.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="report">What the doctor found.</param>
    /// <param name="style">Whether to add color and a kaomoji. Without either, the text is identical.</param>
    public static void Write(TextWriter output, DoctorReport report, Style style)
    {
        var ink = new Ink(output, style);
        ink.Write("AXM DOCTOR", Palette.Accent).Write(" // ", Palette.Dim).Write(Count(report.Assets.Count, "asset")).Line();
        ink.Line();

        var nameWidth = report.Assets.Count == 0 ? 0 : report.Assets.Max(asset => asset.Name.Length) + 3;
        var index = 0;
        foreach (var group in report.Assets.GroupBy(asset => asset.Kind))
        {
            ink.Write("  ").Write(group.Key.Folder().ToUpperInvariant(), Palette.Dim).Line();
            foreach (var asset in group)
            {
                index++;
                WriteRow(ink, asset, index, nameWidth, report);
            }

            ink.Line();
        }

        foreach (var diagnostic in report.Diagnostics)
        {
            WriteDiagnostic(ink, diagnostic);
            ink.Line();
        }

        var (errors, warnings) = (report.ErrorCount, report.WarningCount);
        ink.Write(Count(report.Assets.Count, "asset"))
            .Write(" · ", Palette.Dim)
            .Write(Count(errors, "error"), errors == 0 ? Palette.Ok : Palette.Error);
        if (warnings > 0)
        {
            ink.Write(" · ", Palette.Dim).Write(Count(warnings, "warning"), Palette.Warning);
        }

        var faceColor = errors > 0 ? Palette.Error : warnings > 0 ? Palette.Warning : Palette.Ok;
        ink.Kaomoji(Kaomoji.ForOutcome(errors, warnings), faceColor).Line();
    }

    private static void WriteRow(Ink ink, DiscoveredAsset asset, int index, int nameWidth, DoctorReport report)
    {
        ink.Write("  ").Write($"{index:00}", Palette.Dim).Write("  ").Write(asset.Name.PadRight(nameWidth), Palette.Bold);

        var diagnostics = report.DiagnosticsFor(asset).ToList();
        if (diagnostics.Any(diagnostic => diagnostic.Severity == Severity.Error))
        {
            ink.Write("ERROR", Palette.Error).Line();
            return;
        }

        ink.Write(asset.Maturity!.PadRight(14), Palette.Maturity(asset.Maturity)).Write(asset.Version!.PadRight(8), Palette.Dim);
        if (diagnostics.Count > 0)
        {
            ink.Write("WARNING", Palette.Warning).Line();
        }
        else
        {
            ink.Write("OK", Palette.Ok).Line();
        }
    }

    // Continuation lines line up under the file, whatever the label's width.
    private static void WriteDiagnostic(Ink ink, Diagnostic diagnostic)
    {
        var label = diagnostic.Severity == Severity.Error ? "ERROR" : "WARNING";
        var indent = new string(' ', label.Length + 2);

        ink.Write(label, diagnostic.Severity == Severity.Error ? Palette.Error : Palette.Warning).Write("  ").Write(diagnostic.File, Palette.Path);
        if (diagnostic.Location is { } location)
        {
            ink.Write($":{location.Line}", Palette.Dim);
        }

        ink.Line();
        ink.Write(indent);
        WriteMessage(ink, diagnostic.Message);
        ink.Line();

        foreach (var detail in diagnostic.Detail)
        {
            ink.Write(indent);
            const string allowed = "Allowed: ";
            if (detail.StartsWith(allowed, StringComparison.Ordinal))
            {
                ink.Write(allowed, Palette.Dim).Write(detail[allowed.Length..], Palette.Ok);
            }
            else
            {
                ink.Write(detail);
            }

            ink.Line();
        }
    }

    // The first quoted value in a message is the bad value, so it gets the error color.
    private static void WriteMessage(Ink ink, string message)
    {
        var open = message.IndexOf('"');
        var close = open < 0 ? -1 : message.IndexOf('"', open + 1);
        if (close < 0)
        {
            ink.Write(message);
            return;
        }

        ink.Write(message[..open]).Write(message[open..(close + 1)], Palette.Error).Write(message[(close + 1)..]);
    }

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}

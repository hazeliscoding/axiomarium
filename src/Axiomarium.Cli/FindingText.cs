using Axiomarium.Cli.Output;
using Axiomarium.Core.Health;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Cli;

/// <summary>How <c>axm explain</c> and <c>axm doctor</c> print an instruction finding, so the two never differ.</summary>
internal static class FindingText
{
    /// <summary>
    /// Writes the severity and the id, then the message and the fix lined up after the severity, and a
    /// blank line.
    /// </summary>
    public static void Write(Ink ink, InstructionFinding finding)
    {
        var (label, color) = finding.Severity switch
        {
            Severity.Error => ("ERROR", Palette.Error),
            Severity.Warning => ("WARNING", Palette.Warning),
            _ => ("INFO", Palette.Dim),
        };
        var indent = new string(' ', label.Length + 2);
        ink.Write(label, color).Write("  ").Write(finding.Id, Palette.Bold).Line();
        ink.Write(indent).Write(finding.Message).Line();
        ink.Write(indent).Write("Fix: ", Palette.Dim).Write(finding.Fix).Line();
        ink.Line();
    }
}

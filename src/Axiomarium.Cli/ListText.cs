using Axiomarium.Cli.Output;
using Axiomarium.Core.Assets;
using Axiomarium.Core.Health;

namespace Axiomarium.Cli;

/// <summary>The inventory <c>axm list</c> prints. It shows the vault and judges nothing beyond marking invalid assets.</summary>
public static class ListText
{
    /// <summary>Writes the heading, one block per kind with a row per asset, and the summary.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="report">What the doctor found, which says which assets are invalid.</param>
    /// <param name="kind">Only this kind, or every kind when <see langword="null"/>.</param>
    /// <param name="harness">
    /// Only assets that support this harness, each with how well, or all assets with every harness they
    /// support when <see langword="null"/>.
    /// </param>
    /// <param name="style">Whether to add color and a kaomoji. Without either, the text is identical.</param>
    public static void Write(TextWriter output, DoctorReport report, AssetKind? kind, string? harness, Style style)
    {
        var ink = new Ink(output, style);
        var assets = Harnesses.Filter(report.Assets, kind, harness);

        ink.Write("AXM LIST", Palette.Accent);
        foreach (var filter in new[] { kind?.Folder(), harness }.OfType<string>())
        {
            ink.Write(" // ", Palette.Dim).Write(filter);
        }

        ink.Write(" // ", Palette.Dim).Write(Count(assets.Count)).Line();
        ink.Line();

        var nameWidth = assets.Count == 0 ? 0 : assets.Max(asset => asset.Name.Length) + 3;
        var index = 0;
        var invalid = 0;
        foreach (var group in assets.GroupBy(asset => asset.Kind))
        {
            ink.Write("  ").Write(group.Key.Folder().ToUpperInvariant(), Palette.Dim).Line();
            foreach (var asset in group)
            {
                index++;
                ink.Write("  ").Write($"{index:00}", Palette.Dim).Write("  ").Write(asset.Name.PadRight(nameWidth), Palette.Bold);
                if (asset.Manifest is not { } manifest || report.DiagnosticsFor(asset).Any(diagnostic => diagnostic.Severity == Severity.Error))
                {
                    invalid++;
                    ink.Write("INVALID", Palette.Error).Line();
                    continue;
                }

                var support = harness is null
                    ? string.Join(" ", Harnesses.All.Where(manifest.Supports.ContainsKey))
                    : $"{harness}: {manifest.Supports[harness]}";
                ink.Write(manifest.Maturity.PadRight(14), Palette.Maturity(manifest.Maturity))
                    .Write(manifest.Version.PadRight(8), Palette.Dim)
                    .Write(support, Palette.Path)
                    .Line();
            }

            ink.Line();
        }

        ink.Write(Count(assets.Count));
        if (invalid > 0)
        {
            ink.Write(" · ", Palette.Dim).Write($"{invalid} invalid", Palette.Error);
        }

        ink.Kaomoji(Kaomoji.ForOutcome(invalid, 0), invalid > 0 ? Palette.Error : Palette.Ok).Line();
        if (invalid > 0)
        {
            ink.Write("Run axm validate to see why.", Palette.Dim).Line();
        }
    }

    private static string Count(int count) => $"{count} asset{(count == 1 ? "" : "s")}";
}

namespace Axiomarium.Cli.Output;

/// <summary>ANSI 256-color codes from the brand palette.</summary>
internal static class Palette
{
    public const string Accent = "1;38;5;205";
    public const string Ok = "38;5;114";
    public const string Error = "1;38;5;203";
    public const string Warning = "38;5;214";
    public const string Path = "38;5;80";
    public const string Dim = "38;5;244";
    public const string Bold = "1";

    public static string Maturity(string maturity) => maturity switch
    {
        "experimental" => "38;5;205",
        "incubating" => "38;5;80",
        "tested" => "38;5;75",
        "stable" => "38;5;114",
        "battle-tested" => "1;38;5;114",
        _ => Dim,
    };
}

/// <summary>Writes text that is colored only when the <see cref="Style"/> allows it.</summary>
/// <param name="writer">Where to write.</param>
/// <param name="style">What the stream allows.</param>
internal sealed class Ink(TextWriter writer, Style style)
{
    /// <summary>Writes <paramref name="text"/>, in <paramref name="color"/> when color is on.</summary>
    public Ink Write(string text, string? color = null)
    {
        if (style.Color && color is not null && text.Length > 0)
        {
            writer.Write($"\u001b[{color}m{text}\u001b[0m");
        }
        else
        {
            writer.Write(text);
        }

        return this;
    }

    /// <summary>Adds two spaces and <paramref name="face"/>, when kaomoji are on.</summary>
    public Ink Kaomoji(string face, string color)
    {
        if (style.Kaomoji)
        {
            writer.Write("  ");
            Write(face, color);
        }

        return this;
    }

    /// <summary>Ends the line.</summary>
    public void Line() => writer.WriteLine();
}

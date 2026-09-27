namespace Axiomarium.Cli.Output;

/// <summary>
/// The only kaomoji axm uses. Each one belongs to an outcome, and the same outcome always gets the
/// same one, so output stays predictable. They always sit next to words that say the same thing.
/// </summary>
public static class Kaomoji
{
    /// <summary>Everything checked out.</summary>
    public const string AllClear = "ヽ(・∀・)ﾉ";

    /// <summary>Warnings, but no errors.</summary>
    public const string WarningsOnly = "(・_・;)";

    /// <summary>One or two errors.</summary>
    public const string SomeErrors = "(╥﹏╥)";

    /// <summary>Three errors or more.</summary>
    public const string ManyErrors = "(╯°□°)╯︵ ┻━┻";

    /// <summary>The command couldn't run.</summary>
    public const string CouldNotRun = "(・・?)";

    /// <summary>Printed after the version.</summary>
    public const string Version = "ᕕ( ᐛ )ᕗ";

    /// <summary>Picks the kaomoji for a result.</summary>
    /// <param name="errors">How many errors were found.</param>
    /// <param name="warnings">How many warnings were found.</param>
    /// <returns><see cref="ManyErrors"/> from 3 errors, <see cref="SomeErrors"/> from 1, <see cref="WarningsOnly"/> with only warnings, else <see cref="AllClear"/>.</returns>
    public static string ForOutcome(int errors, int warnings) => errors switch
    {
        >= 3 => ManyErrors,
        >= 1 => SomeErrors,
        _ => warnings > 0 ? WarningsOnly : AllClear,
    };
}

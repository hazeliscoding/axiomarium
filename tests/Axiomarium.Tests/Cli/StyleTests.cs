using Axiomarium.Cli.Output;

namespace Axiomarium.Tests.Cli;

public class StyleTests
{
    [Theory]
    [InlineData(true, null, null, true, false, false)]
    [InlineData(false, null, null, true, true, true)]
    [InlineData(false, "AXM_PLAIN", "1", true, false, false)]
    [InlineData(false, "NO_COLOR", "1", true, false, true)]
    [InlineData(false, "NO_COLOR", "", true, true, true)]
    [InlineData(false, "TERM", "dumb", true, false, true)]
    [InlineData(false, null, null, false, false, true)]
    public void Style_follows_the_terminal_and_the_environment(
        bool redirected, string? variable, string? value, bool virtualTerminal, bool color, bool kaomoji)
    {
        var environment = new Dictionary<string, string?>();
        if (variable is not null)
        {
            environment[variable] = value;
        }

        var style = Style.For(redirected, environment, virtualTerminal);

        Assert.Equal(new Style(color, kaomoji), style);
    }
}

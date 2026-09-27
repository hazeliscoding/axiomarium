using Axiomarium.Cli;

namespace Axiomarium.Tests.Cli;

public class VersionTests
{
    [Fact]
    public void Prints_the_informational_version()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AxmCli.Run(
            ["--version"],
            output,
            error,
            new Dictionary<string, string?>(),
            outputRedirected: true,
            errorRedirected: true,
            virtualTerminal: false,
            Environment.CurrentDirectory);

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Equal("0.1.0-dev", output.ToString().Trim());
    }
}

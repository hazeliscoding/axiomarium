using Axiomarium.Cli;

namespace Axiomarium.Tests.Cli;

// These run dotnet itself, which every machine that runs the tests has, and which reads no harness files.
public class ProcessHarnessRunnerTests
{
    private static HarnessOutput Run(string command, string[] arguments, Func<string, bool>? stopAfter = null)
    {
        using var folder = new TempVault();
        return new ProcessHarnessRunner().RunAsync(new HarnessCall(command, arguments, "", folder.Root, stopAfter, TimeSpan.FromMinutes(1))).GetAwaiter().GetResult();
    }

    [Fact]
    public void A_session_that_runs_to_the_end_returns_its_lines_and_exit_code()
    {
        var output = Run("dotnet", ["--version"]);

        Assert.True(output.Started);
        Assert.Equal(0, output.ExitCode);
        Assert.Matches(@"^\d+\.\d+", Assert.Single(output.Lines));
    }

    [Fact]
    public void A_session_is_stopped_after_the_line_that_ends_it_and_has_no_exit_code()
    {
        var output = Run("dotnet", ["--info"], stopAfter: _ => true);

        Assert.True(output.Started);
        Assert.Single(output.Lines);
        Assert.Null(output.ExitCode);
    }

    [Fact]
    public void A_command_that_is_not_installed_is_not_started()
    {
        var output = Run("axm-no-such-harness", []);

        Assert.Equal((false, "axm-no-such-harness isn't on PATH."), (output.Started, output.Error));
        Assert.Empty(output.Lines);
    }
}

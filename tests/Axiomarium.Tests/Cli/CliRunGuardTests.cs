namespace Axiomarium.Tests.Cli;

// The guards are why no test runs a model or reads the real machine, so each one is shown to fire.
public class CliRunGuardTests
{
    [Theory]
    [InlineData("triggers", "generate", "x")]
    [InlineData("triggers", "test")]
    [InlineData("eval", "run")]
    [InlineData("eval", "compare", "x")]
    [InlineData("conflicts", "a.md", "--judge")]
    public void A_command_that_calls_a_model_needs_a_runner_that_replays_streams(params string[] args)
    {
        using var vault = new TempVault();

        var problem = Assert.Throws<InvalidOperationException>(() => CliRun.Run(args, machine: TestMachine.For(vault.Root)));

        Assert.Contains("starts harness sessions", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("evidence")]
    [InlineData("evidence", "check")]
    [InlineData("evidence", "record", "tests")]
    public void An_evidence_command_needs_a_runner_that_answers_for_git_and_commands(params string[] args)
    {
        var problem = Assert.Throws<InvalidOperationException>(() => CliRun.Run(args));

        Assert.Contains("runs git and commands", problem.Message, StringComparison.Ordinal);
        Assert.Equal(0, CliRun.Run([.. args, "--help"]).ExitCode);
    }

    [Fact]
    public void Help_and_conflicts_without_judge_call_no_model_so_need_no_runner()
    {
        using var vault = new TempVault().Write("a.md", "# A\n");

        Assert.Equal(0, CliRun.Run(["conflicts", "--help"]).ExitCode);
        Assert.Equal(2, CliRun.Run(["conflicts", "a.md"], currentDirectory: vault.Root).ExitCode);
    }
}

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

    // axm evidence record runs a check's command this way, and its exit code is what the record and CI go by.
    [Fact]
    public async Task An_attached_command_shares_axm_s_streams_and_returns_its_exit_code()
    {
        using var folder = new TempVault();
        var runner = new ProcessHarnessRunner();
        var cancellation = TestContext.Current.CancellationToken;

        var passed = await runner.RunAsync(new HarnessCall("dotnet", ["--version"], "", folder.Root, null, Timeout.InfiniteTimeSpan) { Attached = true }, cancellation);
        var failed = await runner.RunAsync(new HarnessCall("dotnet", ["axm-no-such-command"], "", folder.Root, null, Timeout.InfiniteTimeSpan) { Attached = true }, cancellation);

        Assert.Equal((true, 0), (passed.Started, passed.ExitCode));
        Assert.Empty(passed.Lines);
        Assert.True(failed.Started);
        Assert.NotEqual(0, failed.ExitCode);
    }

    // A check's run entry can name a script in the repo by its path, which isn't on PATH.
    [Fact]
    public async Task A_program_named_by_its_path_runs_from_the_folder_and_one_that_can_t_run_is_not_started()
    {
        using var folder = new TempVault();
        var script = OperatingSystem.IsWindows() ? "tool.cmd" : "tool";
        folder.Write(script, OperatingSystem.IsWindows() ? "@exit /b 3\r\n" : "#!/bin/sh\nexit 3\n").Write("notes.txt", "not a program\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path.Combine(folder.Root, script), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var runner = new ProcessHarnessRunner();
        Task<HarnessOutput> Run(string program) => runner.RunAsync(new HarnessCall(program, [], "", folder.Root, null, TimeSpan.FromMinutes(1)), TestContext.Current.CancellationToken);

        var tool = await Run("./tool");
        var missing = await Run("./missing");
        var notes = await Run("./notes.txt");

        Assert.Equal((true, 3), (tool.Started, tool.ExitCode));
        Assert.Equal((false, "./missing isn't there."), (missing.Started, missing.Error));
        Assert.False(notes.Started);
        Assert.StartsWith("./notes.txt couldn't start: ", notes.Error, StringComparison.Ordinal);
    }
}

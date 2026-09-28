using System.Text.Json.Nodes;
using Axiomarium.Cli;

namespace Axiomarium.Tests.Cli;

public class HookCommandTests
{
    private static string EditPayload(TempVault repo, string relativeFile)
    {
        var path = Path.Combine(RepoRoot.Path, "tests", "Axiomarium.Tests", "Fixtures", "ClaudeCode", "post-tool-use-edit.json");
        var payload = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        payload["cwd"] = repo.Root;
        payload["tool_input"]!["file_path"] = Path.Combine(repo.Root, relativeFile);
        return payload.ToJsonString();
    }

    [Fact]
    public void An_edit_outside_the_scope_prints_the_warning_and_exits_0()
    {
        using var repo = new TempVault().Write(".axm/scope", "src/api/**\n");

        var (exitCode, output, error) = CliRun.Run(["hook", "scope-sheriff"], stdin: EditPayload(repo, "src/billing/invoice.txt"));

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Empty(error);
        var context = JsonNode.Parse(output)!["hookSpecificOutput"]!["additionalContext"]!.GetValue<string>();
        Assert.StartsWith("src/billing/invoice.txt is outside this task's scope", context);
    }

    [Fact]
    public void An_edit_inside_the_scope_prints_nothing()
    {
        using var repo = new TempVault().Write(".axm/scope", "src/billing/**\n");

        var (exitCode, output, error) = CliRun.Run(["hook", "scope-sheriff"], stdin: EditPayload(repo, "src/billing/invoice.txt"));

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    [Fact]
    public void Output_is_json_even_in_a_terminal()
    {
        using var repo = new TempVault().Write(".axm/scope", "src/api/**\n");

        var (_, output, _) = CliRun.Run(["hook", "scope-sheriff"], terminal: true, stdin: EditPayload(repo, "src/billing/invoice.txt"));

        Assert.DoesNotContain("\u001b[", output);
        Assert.NotNull(JsonNode.Parse(output));
    }

    // Claude Code reads exit code 2 as "block", so a hook that can't run exits with 1.
    [Fact]
    public void Bad_input_exits_1_not_2()
    {
        var (exitCode, output, error) = CliRun.Run(["hook", "scope-sheriff"], stdin: "not json");

        Assert.Equal(AxmCli.HookCouldNotRun, exitCode);
        Assert.Equal(1, AxmCli.HookCouldNotRun);
        Assert.Empty(output);
        Assert.StartsWith("axm: The hook input isn't valid JSON.", error);
    }

    [Theory]
    [InlineData("hook", "scope-sheriff", "--bogus")]
    [InlineData("hook", "no-such-hook")]
    public void Bad_hook_arguments_exit_1_not_2(params string[] args)
    {
        var (exitCode, _, error) = CliRun.Run(args);

        Assert.Equal(AxmCli.HookCouldNotRun, exitCode);
        Assert.StartsWith("axm: ", error);
    }

    private static string SessionStartPayload(string cwd)
    {
        var path = Path.Combine(RepoRoot.Path, "tests", "Axiomarium.Tests", "Fixtures", "ClaudeCode", "session-start.json");
        var payload = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        payload["cwd"] = cwd;
        return payload.ToJsonString();
    }

    [Fact]
    public void Session_doctor_prints_a_notice_for_a_repo_with_problems()
    {
        using var repo = new TempVault().Write("CLAUDE.md", "project\n").Write("AGENTS.md", "agents\n");

        var (exitCode, output, error) = CliRun.Run(
            ["hook", "session-doctor"], terminal: true, stdin: SessionStartPayload(repo.Root), machine: TestMachine.For(repo.Root));

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Empty(error);
        Assert.DoesNotContain("\u001b[", output);
        var notice = JsonNode.Parse(output)!;
        Assert.StartsWith("axm doctor found 1 warning in this repo: agents-md-hidden in AGENTS.md.", notice["systemMessage"]!.GetValue<string>());
        Assert.Contains("- WARNING agents-md-hidden: ", notice["hookSpecificOutput"]!["additionalContext"]!.GetValue<string>());
    }

    [Fact]
    public void Session_doctor_is_silent_for_a_healthy_repo()
    {
        using var repo = new TempVault().Write("CLAUDE.md", "@AGENTS.md\n").Write("AGENTS.md", "agents\n");

        var (exitCode, output, error) = CliRun.Run(["hook", "session-doctor"], stdin: SessionStartPayload(repo.Root), machine: TestMachine.For(repo.Root));

        Assert.Equal((AxmCli.Passed, "", ""), (exitCode, output, error));
    }

    [Fact]
    public void Session_doctor_exits_1_not_2_when_the_folder_is_gone()
    {
        using var repo = new TempVault();

        var (exitCode, output, error) = CliRun.Run(
            ["hook", "session-doctor"], stdin: SessionStartPayload(Path.Combine(repo.Root, "nowhere")), machine: TestMachine.For(repo.Root));

        Assert.Equal(AxmCli.HookCouldNotRun, exitCode);
        Assert.Empty(output);
        Assert.StartsWith("axm: The folder ", error);
    }
}

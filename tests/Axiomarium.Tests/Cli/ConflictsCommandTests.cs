using System.Text.Json.Nodes;
using Axiomarium.Cli;

namespace Axiomarium.Tests.Cli;

// The judge's answers are scripted, so no test runs a model.
public class ConflictsCommandTests
{
    private const string Answer = """
        {"contradictions": [
          {"first": {"file": "CLAUDE.md", "quote": "Always indent with tabs."}, "second": {"file": "AGENTS.md", "quote": "Indent with two spaces, never tabs."}, "why": "One says tabs, the other spaces."},
          {"first": {"file": "CLAUDE.md", "quote": "Use semicolons."}, "second": {"file": "AGENTS.md", "quote": "Never use semicolons."}, "why": "Made up."}
        ]}
        """;

    // Claude Code loads both files, through the import. Codex loads only AGENTS.md.
    private static TempVault Repo() => new TempVault()
        .Folder("repo/.git")
        .Folder("home/.codex")
        .Write("repo/CLAUDE.md", "@AGENTS.md\n\nAlways indent with tabs.\n")
        .Write("repo/AGENTS.md", "# Rules\n\nIndent with two spaces, never tabs.\n")
        .Write("repo/src/app.ts", "export const a = 1;\n");

    private static (int ExitCode, string Output, string Error) Conflicts(TempVault repo, FakeRunner runner, params string[] flags) =>
        CliRun.Run(["conflicts", "src/app.ts", .. flags], machine: TestMachine.For(repo.Root), currentDirectory: Path.Combine(repo.Root, "repo"), runner: runner);

    private static HarnessOutput CodexAnswer(string text) => new(
        true,
        [
            new JsonObject { ["type"] = "thread.started", ["thread_id"] = "t1" }.ToJsonString(),
            new JsonObject { ["type"] = "item.completed", ["item"] = new JsonObject { ["id"] = "item_0", ["type"] = "agent_message", ["text"] = text } }.ToJsonString(),
            new JsonObject { ["type"] = "turn.completed", ["usage"] = new JsonObject { ["input_tokens"] = 10 } }.ToJsonString(),
        ],
        0,
        "");

    [Fact]
    public void Without_judge_it_says_a_model_is_needed()
    {
        using var repo = Repo();

        var (exitCode, _, error) = Conflicts(repo, new FakeRunner());

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.Contains("axm conflicts asks a model, and only with --judge.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_each_contradiction_the_judge_quoted_from_the_files_as_model_judgment()
    {
        using var repo = Repo();
        var runner = new FakeRunner(FakeRunner.ClaudeAnswer(Answer));

        var (exitCode, output, error) = Conflicts(repo, runner, "--judge", "--harness", "claude-code");

        Assert.Equal((AxmCli.Passed, ""), (exitCode, error));
        Assert.Equal(
            """
            AXM CONFLICTS // src/app.ts · judged by Claude Code on claude-opus-5-5 · model judgment

              CLAUDE CODE // 2 instruction files
              01  CLAUDE.md:3  "Always indent with tabs."
                  AGENTS.md:3  "Indent with two spaces, never tabs."
                  why, in the model's words: One says tabs, the other spaces.
              1 more was dropped: its quotes aren't in the files.

            1 contradiction · the model's judgment, so a rerun can differ

            """.ReplaceLineEndings("\n"),
            output.ReplaceLineEndings("\n"));
        var brief = Assert.Single(runner.Calls).Input;
        Assert.Contains("=== FILE: CLAUDE.md ===\n@AGENTS.md\n\nAlways indent with tabs.\n", brief, StringComparison.Ordinal);
        Assert.Contains("=== FILE: AGENTS.md ===\n", brief, StringComparison.Ordinal);
    }

    // Each distinct set of files is judged once, and a harness can be the judge for all of them.
    [Fact]
    public void Codex_can_judge_and_each_harness_s_files_are_judged_once()
    {
        using var repo = Repo();
        var runner = new FakeRunner(call => CodexAnswer("""{"contradictions": []}"""));

        var (exitCode, output, _) = Conflicts(repo, runner, "--judge", "--judge-with", "codex");

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Equal(2, runner.Calls.Count);
        Assert.All(runner.Calls, call => Assert.Equal(["exec", "--json", "-s", "read-only", "--ephemeral", "--skip-git-repo-check", "--ignore-rules", "-"], call.Arguments));
        Assert.Contains("0 contradictions · the model's judgment, so a rerun can differ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void An_answer_that_can_t_be_read_twice_can_t_run()
    {
        using var repo = Repo();
        var runner = new FakeRunner(FakeRunner.ClaudeAnswer("I think they're fine."), FakeRunner.ClaudeAnswer("Still fine."));

        var (exitCode, _, error) = Conflicts(repo, runner, "--judge", "--harness", "claude-code");

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.Contains("The judge's answer couldn't be used, twice: the answer isn't JSON.", error, StringComparison.Ordinal);
        Assert.Contains("Your previous answer couldn't be used: the answer isn't JSON.", runner.Calls[1].Input, StringComparison.Ordinal);
    }
}

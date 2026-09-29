using Axiomarium.Cli;

namespace Axiomarium.Tests.Cli;

public class TriggersTestCommandTests
{
    private const string Prompts = """
        skill: deploy
        prompts:
          - prompt: Ship the shop.
            kind: positive
            should_trigger: true
          - prompt: Format this table.
            kind: negative
            should_trigger: false

        """;

    private static TempVault Shop(bool prompts = true)
    {
        var vault = new TempVault()
            .Folder("repo/.git")
            .Folder("home/.codex")
            .Write("repo/skills/deploy/asset.yaml", TempVault.Manifest("skill", "deploy")
                .Replace("description: Reviews a codebase for decisions an LLM shouldn't own.", "description: Deploys the shop to production.")
                .Replace("use_when: Writing or changing an asset.", "use_when: Releasing the shop.")
                .Replace("  claude-code: experimental\n", "  claude-code: experimental\n  codex: experimental\n"))
            .Write("repo/skills/deploy/skill.md", "# Deploy\n")
            .Write("repo/.claude/skills/ship/SKILL.md", "---\ndescription: Ships the shop to production.\n---\nSteps.\n")
            .Write("repo/.agents/skills/ship/SKILL.md", "---\ndescription: Ships the shop to production.\n---\nSteps.\n");
        return prompts ? vault.Write("repo/skills/deploy/evals/trigger/prompts.yaml", Prompts) : vault;
    }

    // Claude Code gets both prompts right. Codex picks ship for the positive prompt and deploy for the negative one.
    private static HarnessOutput Respond(HarnessCall call) => (call.Command, call.Arguments[0], call.Input) switch
    {
        (_, "--version", _) => FakeRunner.Version(call),
        ("claude", _, "Ship the shop.") => FakeRunner.ClaudePick("deploy"),
        ("claude", _, _) => FakeRunner.ClaudePick(),
        (_, _, "Ship the shop.") => FakeRunner.CodexRead("ship"),
        _ => FakeRunner.CodexRead("deploy"),
    };

    private static (int ExitCode, string Output, string Error) Test(TempVault vault, IHarnessRunner runner, params string[] flags) =>
        CliRun.Run(
            ["triggers", "test", "--runs", "1", .. flags],
            machine: TestMachine.For(vault.Root),
            currentDirectory: Path.Combine(vault.Root, "repo"),
            runner: runner);

    [Fact]
    public void Reports_each_harness_s_scores_and_every_problem_run_with_its_cause()
    {
        using var vault = Shop();

        var (exitCode, output, error) = Test(vault, new FakeRunner(Respond) { FolderRoot = vault.Root });

        Assert.Equal((AxmCli.Passed, ""), (exitCode, error));
        Assert.Equal(
            """
            AXM TRIGGERS TEST // 1 skill · 2 prompts × 1 run · 2 harnesses · 4 sessions, 4 at a time

              CLAUDE CODE 2.1.284 // picks by claude-opus-5-5
              01  deploy   precision 1.00 (1 of 1)     recall 1.00 (1 of 1)

              CODEX 0.156.1 // picks by its configured model
              02  deploy   precision 0.00 (0 of 1)     recall 0.00 (0 of 1)

              COLLISIONS
              --  "Ship the shop."  picked ship in 1 of 1 run, expected deploy
                  the prompt shares ship and shop with ship, and shop with deploy

              FALSE TRIGGERS
              --  "Format this table."  picked deploy in 1 of 1 run, where it shouldn't fire
                  no cause found in the listing

            Picks are made by each harness's model, so a rerun can differ.
            4 runs scored · 2 with a problem · 0 failed

            """,
            output);
    }

    [Fact]
    public void A_listing_over_budget_is_noted_once_under_its_harness()
    {
        using var vault = Shop().Write("home/.claude/settings.json", """{ "skillListingBudgetFraction": 0.0001 }""");

        var (_, output, _) = Test(vault, new FakeRunner(Respond) { FolderRoot = vault.Root }, "--harness", "claude-code");

        Assert.Contains("  CLAUDE CODE 2.1.284 // picks by claude-opus-5-5\n  note: its skill listing is over budget, ", output);
        Assert.Contains(" of 80 characters assuming a 200k-token context window, so some skills are listed by name only\n  01  deploy", output);
    }

    [Fact]
    public void A_harness_that_is_not_installed_is_skipped_and_the_other_still_runs()
    {
        using var vault = Shop();
        var runner = new FakeRunner(call => call.Command == "codex" ? new HarnessOutput(false, [], null, "codex isn't on PATH.") : Respond(call)) { FolderRoot = vault.Root };

        var (exitCode, output, _) = Test(vault, runner);

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Contains("  CODEX // skipped: codex isn't on PATH.\n", output);
        Assert.Equal(["--version", "-p", "-p"], runner.Calls.Where(call => call.Command == "claude").Select(call => call.Arguments[0]));
        Assert.Single(runner.Calls, call => call.Command == "codex");
    }

    [Fact]
    public void With_neither_harness_installed_it_cannot_run()
    {
        using var vault = Shop();

        var (exitCode, _, error) = Test(vault, new FakeRunner(call => new HarnessOutput(false, [], null, $"{call.Command} isn't on PATH.")) { FolderRoot = vault.Root });

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.StartsWith("axm: No harness to test on. claude isn't on PATH. codex isn't on PATH.", error);
    }

    [Fact]
    public void Sessions_that_gave_no_answer_are_listed_and_not_scored()
    {
        using var vault = Shop();
        var runner = new FakeRunner(call => call is { Command: "codex", Arguments: ["exec", ..] }
            ? new HarnessOutput(true, ["""{"type":"turn.failed","error":{"message":"You've hit your usage limit."}}"""], 1, "")
            : Respond(call))
        { FolderRoot = vault.Root };

        var (_, output, _) = Test(vault, runner);

        Assert.Contains("  FAILED // 2 sessions gave no answer and aren't scored\n  --  codex  2 sessions: You've hit your usage limit.\n", output);
        Assert.EndsWith("2 runs scored · 0 with a problem · 2 failed\n", output);
    }

    [Fact]
    public void Without_prompts_it_says_to_generate_them_first()
    {
        using var vault = Shop(prompts: false);

        var (exitCode, _, error) = Test(vault, new FakeRunner() { FolderRoot = vault.Root });

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.StartsWith("axm: No vault skill has trigger prompts yet.\n     Run axm triggers generate <skill> first.", error);
    }
}

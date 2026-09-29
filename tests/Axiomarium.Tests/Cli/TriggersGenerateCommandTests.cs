using Axiomarium.Cli;

namespace Axiomarium.Tests.Cli;

public class TriggersGenerateCommandTests
{
    private const string Answer = """
        {"prompts": [
          {"prompt": "Ship the shop.", "kind": "positive", "should_trigger": true},
          {"prompt": "Format this table.", "kind": "negative", "should_trigger": false},
          {"prompt": "Which one ships it?", "kind": "ambiguous", "should_trigger": false, "rival": "ship"}
        ]}
        """;

    private const string Preview = """
        AXM TRIGGERS GENERATE // deploy // 3 prompts from claude-opus-5-5 in Claude Code 2.1.284

          POSITIVE // should pick deploy
          01  Ship the shop.

          NEGATIVE // shouldn't pick deploy
          02  Format this table.

          AMBIGUOUS // between deploy and a rival
          03  Which one ships it?  picks ship

        A model wrote these prompts. Review them before you rely on them.

        """;

    private const string Written = """
        # yaml-language-server: $schema=../../../../schemas/trigger-prompts.schema.json
        # A model wrote these prompts. Review them before you rely on them.
        skill: deploy
        generated:
          by: claude-code
          model: "claude-opus-5-5"
          date: "2026-09-28"
        prompts:
          - prompt: "Ship the shop."
            kind: positive
            should_trigger: true
          - prompt: "Format this table."
            kind: negative
            should_trigger: false
          - prompt: "Which one ships it?"
            kind: ambiguous
            should_trigger: false
            rival: "ship"

        """;

    private static TempVault Shop() => new TempVault()
        .Folder("repo/.git")
        .Folder("home/.codex")
        .Write("repo/skills/deploy/asset.yaml", TempVault.Manifest("skill", "deploy")
            .Replace("description: Reviews a codebase for decisions an LLM shouldn't own.", "description: Deploys the shop to production.")
            .Replace("use_when: Writing or changing an asset.", "use_when: Releasing the shop."))
        .Write("repo/skills/deploy/skill.md", "# Deploy\n")
        .Write("repo/.claude/skills/ship/SKILL.md", "---\ndescription: Ships the shop to production.\n---\nSteps.\n");

    private static string Target(TempVault vault) => Path.Combine(vault.Root, "repo", "skills", "deploy", "evals", "trigger", "prompts.yaml");

    private static (int ExitCode, string Output, string Error) Generate(
        TempVault vault, FakeRunner runner, string[]? flags = null, string stdin = "", bool terminal = false) =>
        CliRun.Run(
            ["triggers", "generate", "deploy", .. flags ?? ["--yes"]],
            machine: TestMachine.For(vault.Root),
            currentDirectory: Path.Combine(vault.Root, "repo"),
            runner: runner,
            clock: new FixedClock(),
            stdin: stdin,
            inputRedirected: !terminal);

    [Fact]
    public void Shows_the_prompts_and_with_yes_writes_them_labeled_as_model_output()
    {
        using var vault = Shop();
        var runner = new FakeRunner(FakeRunner.ClaudeAnswer(Answer));

        var (exitCode, output, error) = Generate(vault, runner);

        Assert.Equal((AxmCli.Passed, ""), (exitCode, error));
        Assert.Equal(
            Preview + "Wrote skills/deploy/evals/trigger/prompts.yaml. Once you've reviewed the prompts, set evals.trigger: true in skills/deploy/asset.yaml.\n",
            output);
        Assert.Equal(Written, File.ReadAllText(Target(vault)));
    }

    [Fact]
    public void Asks_claude_code_for_one_turn_with_no_tools_and_briefs_it_with_the_skill_and_its_rivals()
    {
        using var vault = Shop();
        var runner = new FakeRunner(FakeRunner.ClaudeAnswer(Answer));

        Generate(vault, runner, ["--yes", "--model", "haiku"]);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(
            ("claude", "-p --tools  --strict-mcp-config --output-format stream-json --verbose --no-session-persistence --model haiku", null),
            (call.Command, string.Join(' ', call.Arguments), call.Folder));
        Assert.Contains("\n- deploy: Deploys the shop to production. - Releasing the shop.\n", call.Input);
        Assert.Contains("\n- ship: Ships the shop to production.\n", call.Input);
    }

    [Fact]
    public void In_a_terminal_it_asks_before_writing_and_writes_nothing_on_no()
    {
        using var yes = Shop();
        using var no = Shop();

        var (_, written, _) = Generate(yes, new FakeRunner(FakeRunner.ClaudeAnswer(Answer)), [], stdin: "y\n", terminal: true);
        var (exitCode, declined, _) = Generate(no, new FakeRunner(FakeRunner.ClaudeAnswer(Answer)), [], stdin: "n\n", terminal: true);

        Assert.Contains("Write skills/deploy/evals/trigger/prompts.yaml? [y/N] Wrote skills/deploy/evals/trigger/prompts.yaml.", CliRun.StripColor(written));
        Assert.True(File.Exists(Target(yes)));
        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.EndsWith("? [y/N] Nothing written.\n", declined);
        Assert.False(File.Exists(Target(no)));
    }

    // Both refusals come before the model call, so a run that can't write costs nothing.
    [Fact]
    public void Without_a_terminal_or_over_an_existing_file_it_refuses_before_calling_the_model()
    {
        using var noTerminal = Shop();
        using var existing = Shop().Write("repo/skills/deploy/evals/trigger/prompts.yaml", "skill: deploy\n");
        var runner = new FakeRunner();

        var (exitCode, _, error) = Generate(noTerminal, runner, []);
        var (existingCode, _, existingError) = Generate(existing, runner);

        Assert.Equal((AxmCli.CouldNotRun, AxmCli.CouldNotRun), (exitCode, existingCode));
        Assert.StartsWith("axm: generate asks before it writes, and there's no terminal to ask in.\n     Pass --yes to write without asking.", error);
        Assert.StartsWith("axm: skills/deploy/evals/trigger/prompts.yaml already exists.\n     Pass --replace to write new prompts over it.", existingError);
        Assert.Empty(runner.Calls);
        Assert.Equal("skill: deploy\n", File.ReadAllText(Target(existing)));
    }

    [Fact]
    public void Replace_writes_over_the_existing_prompts()
    {
        using var vault = Shop().Write("repo/skills/deploy/evals/trigger/prompts.yaml", "skill: deploy\n");

        var (exitCode, _, _) = Generate(vault, new FakeRunner(FakeRunner.ClaudeAnswer(Answer)), ["--yes", "--replace"]);

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Equal(Written, File.ReadAllText(Target(vault)));
    }

    [Fact]
    public void An_unusable_answer_is_retried_once_with_the_reason()
    {
        using var vault = Shop();
        var runner = new FakeRunner(FakeRunner.ClaudeAnswer("Sure, here are some prompts."), FakeRunner.ClaudeAnswer(Answer));

        var (exitCode, _, _) = Generate(vault, runner);

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Equal(2, runner.Calls.Count);
        Assert.EndsWith("Your previous answer couldn't be used: the answer isn't JSON. Answer again with only the JSON.\n", runner.Calls[1].Input);
        Assert.True(File.Exists(Target(vault)));
    }

    [Fact]
    public void Two_unusable_answers_write_nothing()
    {
        using var vault = Shop();
        var runner = new FakeRunner(FakeRunner.ClaudeAnswer("No."), FakeRunner.ClaudeAnswer("""{"prompts": []}"""));

        var (exitCode, output, error) = Generate(vault, runner);

        Assert.Equal((AxmCli.CouldNotRun, ""), (exitCode, output));
        Assert.StartsWith("axm: Claude Code's answer couldn't be used, twice: prompts needs at least 1 item. Nothing was written.", error);
        Assert.False(File.Exists(Target(vault)));
    }

    [Fact]
    public void Without_claude_code_on_path_it_says_so()
    {
        using var vault = Shop();

        var (exitCode, _, error) = Generate(vault, new FakeRunner(new HarnessOutput(false, [], null, "claude isn't on PATH.")));

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.StartsWith("axm: Couldn't start Claude Code: claude isn't on PATH.\n     Install Claude Code and log in, then run this again.", error);
    }
}

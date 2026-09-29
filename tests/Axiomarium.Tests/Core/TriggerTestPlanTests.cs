using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Tests.Core;

public class TriggerTestPlanTests
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

    private static string Manifest(string name, bool codex = false) =>
        TempVault.Manifest("skill", name).Replace("  claude-code: experimental\n", codex ? "  claude-code: experimental\n  codex: experimental\n" : "  claude-code: experimental\n");

    private static TempVault Vault(bool codex = false) => new TempVault()
        .Folder("repo/.git")
        .Folder("home/.codex")
        .Write("repo/skills/deploy/asset.yaml", Manifest("deploy", codex))
        .Write("repo/skills/deploy/skill.md", "# Deploy\n")
        .Write("repo/skills/deploy/evals/trigger/prompts.yaml", Prompts)
        .Write("repo/skills/tables/asset.yaml", Manifest("tables"))
        .Write("repo/skills/tables/skill.md", "# Tables\n");

    private static TriggerTestSetup Plan(TempVault vault, string[] skills, Harness[]? harnesses = null, int runs = 3) =>
        TriggerTest.Plan(Path.Combine(vault.Root, "repo"), skills, harnesses ?? [Harness.ClaudeCode, Harness.Codex], runs, TestMachine.For(vault.Root));

    [Fact]
    public void Every_vault_skill_with_prompts_runs_each_prompt_the_given_number_of_times_on_each_harness_it_supports()
    {
        using var vault = Vault(codex: true);

        var plan = Plan(vault, []).Plan!;

        Assert.Equal(["deploy"], plan.Prompts.Keys);
        Assert.Equal(12, plan.Sessions.Count);
        Assert.Equal(
            [(Harness.ClaudeCode, 0, 1, "Ship the shop."), (Harness.ClaudeCode, 0, 2, "Ship the shop.")],
            plan.Sessions.Take(2).Select(session => (session.Harness, session.Prompt, session.Run, session.Text)));
        Assert.Equal([Harness.ClaudeCode, Harness.Codex], plan.Sessions.Select(session => session.Harness).Distinct());
        Assert.Contains(".claude/skills/deploy/SKILL.md", plan.Workspace.Write.Select(file => file.Path));
    }

    [Fact]
    public void A_skill_runs_only_on_the_harnesses_it_supports_and_the_ones_asked_for()
    {
        using var vault = Vault();

        var claudeOnly = Plan(vault, [], runs: 1).Plan!;
        var none = Plan(vault, [], [Harness.Codex]);

        Assert.Equal([Harness.ClaudeCode], claudeOnly.Sessions.Select(session => session.Harness).Distinct());
        Assert.Equal(("None of the skills with prompts supports codex.", null), (none.Problem, none.Hint));
    }

    [Fact]
    public void A_skill_without_prompts_says_to_generate_them()
    {
        using var vault = Vault();
        using var empty = new TempVault().Folder("repo/.git").Write("repo/skills/tables/asset.yaml", Manifest("tables")).Write("repo/skills/tables/skill.md", "# Tables\n");

        var named = Plan(vault, ["tables"]);
        var missing = Plan(vault, ["release"]);
        var nothing = TriggerTest.Plan(Path.Combine(empty.Root, "repo"), [], [Harness.ClaudeCode], 3, TestMachine.For(empty.Root));

        Assert.Equal(("tables has no trigger prompts yet.", "Run axm triggers generate tables first."), (named.Problem, named.Hint));
        Assert.Equal(("The vault has no skill named release.", "Its skills: deploy, tables."), (missing.Problem, missing.Hint));
        Assert.Equal(("No vault skill has trigger prompts yet.", "Run axm triggers generate <skill> first."), (nothing.Problem, nothing.Hint));
    }

    [Fact]
    public void A_prompt_file_with_errors_stops_the_run()
    {
        using var vault = Vault().Write("repo/skills/deploy/evals/trigger/prompts.yaml", "skill: deploy\nprompts: []\n");

        var setup = Plan(vault, []);

        Assert.Equal(("skills/deploy/evals/trigger/prompts.yaml has errors.", "Run axm validate to see them."), (setup.Problem, setup.Hint));
    }
}

using Axiomarium.Core.Evals;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class EvalRunPlanTests
{
    private const string Case = "prompt: Ship it.\nchecks:\n  - reply: shipped\n";

    private static string Manifest(string kind, string name, bool codex) =>
        TempVault.Manifest(kind, name).Replace("  claude-code: experimental\n", codex ? "  claude-code: experimental\n  codex: experimental\n" : "  claude-code: experimental\n");

    private static TempVault Vault() => new TempVault()
        .Folder("repo/.git")
        .InRepo("skills/deploy", Manifest("skill", "deploy", codex: true))
        .Write("repo/skills/deploy/evals/behavioral/ships/eval.yaml", Case)
        .Write("repo/skills/deploy/evals/behavioral/ships/repo/app.ts", "export const a = 1;\n")
        .Write("repo/skills/deploy/evals/regression/pushed-to-main/eval.yaml", Case + "guards: It pushed to main.\n")
        .InRepo("hooks/scope-sheriff", Manifest("hook", "scope-sheriff", codex: false))
        .Write("repo/hooks/scope-sheriff/evals/behavioral/warns/eval.yaml", Case)
        .InRepo("skills/tables", Manifest("skill", "tables", codex: true));

    private static EvalRunSetup Plan(TempVault vault, string[] assets, Harness[]? harnesses = null, int runs = 2) =>
        EvalRuns.Plan(Path.Combine(vault.Root, "repo"), assets, harnesses ?? [Harness.ClaudeCode, Harness.Codex], runs, TestMachine.For(vault.Root));

    [Fact]
    public void Every_case_runs_the_given_number_of_times_on_each_harness_its_asset_supports()
    {
        using var vault = Vault();

        var plan = Plan(vault, []).Plan!;

        Assert.Equal(
            [
                ("hooks/scope-sheriff", "warns", Harness.ClaudeCode, 1), ("hooks/scope-sheriff", "warns", Harness.ClaudeCode, 2),
                ("skills/deploy", "ships", Harness.ClaudeCode, 1), ("skills/deploy", "ships", Harness.ClaudeCode, 2),
                ("skills/deploy", "pushed-to-main", Harness.ClaudeCode, 1), ("skills/deploy", "pushed-to-main", Harness.ClaudeCode, 2),
                ("skills/deploy", "ships", Harness.Codex, 1), ("skills/deploy", "ships", Harness.Codex, 2),
                ("skills/deploy", "pushed-to-main", Harness.Codex, 1), ("skills/deploy", "pushed-to-main", Harness.Codex, 2),
            ],
            plan.Sessions.Select(session => (session.Asset.Folder, session.Case.Name, session.Harness, session.Run)));
        Assert.Equal(
            Path.Combine(vault.Root, "repo", "skills", "deploy", "evals", "behavioral", "ships"),
            plan.Sessions.First(session => session.Case.Name == "ships").CaseFolder);
    }

    [Fact]
    public void An_asset_is_named_by_its_name_or_by_its_folder_when_two_share_a_name()
    {
        using var vault = Vault()
            .InRepo("agents/deploy", TempVault.Manifest("agent", "deploy"))
            .Write("repo/agents/deploy/evals/behavioral/audits/eval.yaml", Case);

        var ambiguous = Plan(vault, ["deploy"]);
        var byFolder = Plan(vault, ["skills/deploy"], [Harness.Codex]).Plan!;

        Assert.Equal(
            ("Two assets are named deploy: agents/deploy and skills/deploy.", "Name one by its folder, such as skills/deploy."),
            (ambiguous.Problem, ambiguous.Hint));
        Assert.Equal(["skills/deploy"], byFolder.Sessions.Select(session => session.Asset.Folder).Distinct());
    }

    [Fact]
    public void Each_problem_says_what_to_do()
    {
        using var vault = Vault();
        using var empty = new TempVault().Folder("repo/.git").InRepo("skills/tables", TempVault.Manifest("skill", "tables"));

        var missing = Plan(vault, ["release"]);
        var bare = Plan(vault, ["tables"]);
        var none = EvalRuns.Plan(Path.Combine(empty.Root, "repo"), [], [Harness.ClaudeCode], 3, TestMachine.For(empty.Root));
        var codexOnly = Plan(vault, ["scope-sheriff"], [Harness.Codex]);

        Assert.Equal(("The vault has no asset named release.", "Its assets: deploy, scope-sheriff, tables."), (missing.Problem, missing.Hint));
        Assert.Equal(("tables has no eval cases yet.", "Add one as skills/tables/evals/behavioral/<case>/eval.yaml."), (bare.Problem, bare.Hint));
        Assert.Equal(("No asset in the vault has eval cases yet.", "Add one as <kind folder>/<asset>/evals/behavioral/<case>/eval.yaml."), (none.Problem, none.Hint));
        Assert.Equal(("None of the assets with eval cases supports codex.", (string?)null), (codexOnly.Problem, codexOnly.Hint));
    }

    [Fact]
    public void A_case_with_errors_stops_the_run()
    {
        using var vault = Vault().Write("repo/skills/deploy/evals/behavioral/ships/eval.yaml", "prompt: Ship it.\nchecks: []\n");

        var setup = Plan(vault, []);

        Assert.Equal(("skills/deploy/evals/behavioral/ships/eval.yaml has errors.", "Run axm validate to see them."), (setup.Problem, setup.Hint));
    }

    // Only skills, hooks and agents can be installed. A policy with cases is left out and said so, unless it's named.
    [Fact]
    public void An_asset_eval_can_t_install_is_left_out_with_a_note()
    {
        using var vault = Vault()
            .InRepo("policies/boundaries", TempVault.Manifest("policy", "boundaries"))
            .Write("repo/policies/boundaries/evals/behavioral/holds/eval.yaml", Case);

        var plan = Plan(vault, []).Plan!;
        var named = Plan(vault, ["boundaries"]);

        Assert.Equal(
            ["policies/boundaries has eval cases, but axm eval runs skills, hooks and agents. A policy's rules are tested through the assets that enforce them."],
            plan.Notes);
        Assert.DoesNotContain(plan.Sessions, session => session.Asset.Folder == "policies/boundaries");
        Assert.Equal("axm eval runs skills, hooks and agents. A policy's rules are tested through the assets that enforce them.", named.Problem);
    }

}

internal static class RepoVaults
{
    private static readonly Dictionary<string, string> ContentFiles = new() { ["skills"] = "skill.md", ["hooks"] = "hook.md", ["agents"] = "agent.md", ["policies"] = "policy.md" };

    /// <summary>Writes an asset under the vault's repo/ folder, with its manifest and content file.</summary>
    public static TempVault InRepo(this TempVault vault, string folder, string manifest) =>
        vault.Write($"repo/{folder}/asset.yaml", manifest).Write($"repo/{folder}/{ContentFiles[folder[..folder.IndexOf('/')]]}", $"# {folder}\n");
}

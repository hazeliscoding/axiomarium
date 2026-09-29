using Axiomarium.Core.Triggers;

namespace Axiomarium.Tests.Core;

public class TriggerGenerationPlanTests
{
    private static TempVault Vault() => new TempVault()
        .Folder("repo/.git")
        .Folder("home/.codex")
        .Write("repo/skills/deploy/asset.yaml", TempVault.Manifest("skill", "deploy")
            .Replace("description: Reviews a codebase for decisions an LLM shouldn't own.", "description: Deploys the shop to production.")
            .Replace("use_when: Writing or changing an asset.", "use_when: Releasing the shop."))
        .Write("repo/skills/deploy/skill.md", "# Deploy\n")
        .Write("repo/.claude/skills/ship/SKILL.md", "---\ndescription: Ships the shop to production.\n---\nSteps.\n");

    private static GenerationSetup Plan(TempVault vault, string skill, string folder = "repo") =>
        TriggerGeneration.Plan(Path.Combine(vault.Root, folder), skill, TestMachine.For(vault.Root));

    [Fact]
    public void A_plan_names_the_skill_its_text_as_sync_would_list_it_its_rivals_and_where_its_prompts_go()
    {
        using var vault = Vault();

        var plan = Plan(vault, "deploy").Plan!;

        Assert.Equal(("deploy", "Deploys the shop to production. - Releasing the shop."), (plan.Skill, plan.Text));
        Assert.Equal([("ship", "Ships the shop to production.")], plan.Rivals);
        Assert.Equal(Path.Combine(vault.Root, "repo", "skills", "deploy", "evals", "trigger", "prompts.yaml"), plan.Target);
        Assert.Equal(Path.Combine(vault.Root, "repo", "skills", "deploy", "asset.yaml"), plan.Manifest);
    }

    [Fact]
    public void A_skill_the_vault_does_not_have_names_the_ones_it_does()
    {
        using var vault = Vault();

        var setup = Plan(vault, "release");

        Assert.Null(setup.Plan);
        Assert.Equal(("The vault has no skill named release.", "Its skills: deploy."), (setup.Problem, setup.Hint));
    }

    [Fact]
    public void A_folder_without_a_vault_or_a_skill_whose_manifest_is_invalid_is_a_problem()
    {
        using var vault = Vault().Folder("empty").Write("repo/skills/deploy/asset.yaml", "name: deploy\n");

        var noVault = Plan(vault, "deploy", "empty");
        var invalid = Plan(vault, "deploy");

        Assert.Equal($"No vault found in {Path.Combine(vault.Root, "empty")} or its repo.", noVault.Problem);
        Assert.Equal(("skills/deploy/asset.yaml has errors, so its description can't be read.", "Run axm validate to see them."), (invalid.Problem, invalid.Hint));
    }
}

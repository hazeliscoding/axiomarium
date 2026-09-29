using Axiomarium.Core.Triggers;

namespace Axiomarium.Tests.Core;

public class TriggerWorkspaceTests
{
    private static TempVault Shop() => new TempVault()
        .Folder("repo/.git")
        .Folder("home/.codex")
        .Write("repo/CLAUDE.md", "Project notes.\nSee @docs/guide.md for the guide.\n")
        .Write("repo/docs/guide.md", "The guide.\n")
        .Write("repo/docs/other.md", "Not loaded at launch.\n")
        .Write("repo/AGENTS.md", "Rules.\n")
        .Write("repo/.mcp.json", "{}\n")
        .Write("repo/.claude/settings.json", "{}\n")
        .Write("repo/.claude/skills/ship/SKILL.md", "---\ndescription: Ships it.\n---\nSteps.\n")
        .Write("repo/.claude/skills/deploy/SKILL.md", "---\ndescription: An older copy.\n---\nOld.\n")
        .Write("repo/.agents/skills/lint/SKILL.md", "---\ndescription: Lints.\n---\nSteps.\n")
        .Write("repo/src/app.ts", "export const a = 1;\n")
        .Write("repo/skills/deploy/asset.yaml", TempVault.Manifest("skill", "deploy")
            .Replace("description: Reviews a codebase for decisions an LLM shouldn't own.", "description: Deploys the shop.")
            .Replace("use_when: Writing or changing an asset.", "use_when: Releasing \"v2\".")
            .Replace("  claude-code: experimental\n", "  claude-code: experimental\n  codex: experimental\n"))
        .Write("repo/skills/deploy/skill.md", "# Deploy\n\nRun the release.\n");

    private static WorkspacePlan Plan(TempVault vault) =>
        TriggerWorkspace.Plan(Path.Combine(vault.Root, "repo"), Path.Combine(vault.Root, "repo"), TestMachine.For(vault.Root));

    // Sessions stop at the first action, so only what shapes the listing and the instructions at launch matters.
    [Fact]
    public void The_copy_holds_the_instructions_what_they_import_and_each_harness_s_folders_and_nothing_else()
    {
        using var vault = Shop();

        var plan = Plan(vault);

        Assert.Equal(
            [".agents/skills/lint/SKILL.md", ".claude/settings.json", ".claude/skills/ship/SKILL.md", ".mcp.json", "AGENTS.md", "CLAUDE.md", "docs/guide.md"],
            plan.Copy);
    }

    // After sync a vault skill is listed under its name, in place of any copy already there.
    [Fact]
    public void Vault_skills_are_written_as_each_supported_harness_s_skill_in_place_of_a_listed_copy()
    {
        using var vault = Shop();

        var plan = Plan(vault);

        Assert.Equal([".agents/skills/deploy/SKILL.md", ".claude/skills/deploy/SKILL.md"], plan.Write.Select(file => file.Path));
        Assert.Equal(
            "---\nname: deploy\ndescription: \"Deploys the shop.\"\nwhen_to_use: \"Releasing \\\"v2\\\".\"\n---\n# Deploy\n\nRun the release.\n",
            plan.Write.Single(file => file.Path.StartsWith(".claude/", StringComparison.Ordinal)).Content);
        Assert.Equal(
            "---\nname: deploy\ndescription: \"Deploys the shop. - Releasing \\\"v2\\\".\"\n---\n# Deploy\n\nRun the release.\n",
            plan.Write.Single(file => file.Path.StartsWith(".agents/", StringComparison.Ordinal)).Content);
        Assert.DoesNotContain(".claude/skills/deploy/SKILL.md", plan.Copy);
    }

    [Fact]
    public void Without_a_vault_only_the_repo_is_copied()
    {
        using var vault = Shop();

        var plan = TriggerWorkspace.Plan(Path.Combine(vault.Root, "repo"), null, TestMachine.For(vault.Root));

        Assert.Empty(plan.Write);
        Assert.Contains(".claude/skills/deploy/SKILL.md", plan.Copy);
    }
}

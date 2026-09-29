using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Tests.Core;

public class TriggerOverlapTests
{
    private static TempVault Repo() => new TempVault().Folder("repo/.git").Folder("home/.codex");

    private static string Skill(string description) => $"---\ndescription: {description}\n---\nSteps.\n";

    private static TriggerReport Check(TempVault vault)
    {
        var result = TriggerOverlap.Check(Path.Combine(vault.Root, "repo"), TestMachine.For(vault.Root));
        Assert.Null(result.Problem);
        return result.Report!;
    }

    private static HarnessOverlap For(TriggerReport report, Harness harness) => report.Harnesses.Single(overlap => overlap.Harness == harness);

    private static (string, string, string)[] Pairs(HarnessOverlap overlap) =>
        [.. overlap.Pairs.Select(pair => (pair.First.Name, pair.Second.Name, string.Join(", ", pair.Shared)))];

    [Fact]
    public void Skills_that_share_rare_terms_overlap_and_name_the_terms()
    {
        using var vault = Repo()
            .Write("repo/.claude/skills/deploy/SKILL.md", Skill("Deploys the shop to production."))
            .Write("repo/.claude/skills/ship/SKILL.md", Skill("Ships a release of the shop to production."))
            .Write("repo/.claude/skills/tables/SKILL.md", Skill("Formats markdown tables."));

        var claude = For(Check(vault), Harness.ClaudeCode);

        Assert.Equal([("deploy", "ship", "production, shop")], Pairs(claude));
        Assert.InRange(claude.Pairs[0].Score, TriggerOverlap.Threshold, 1);
        Assert.Equal(["deploy", "ship", "tables"], claude.Compared.Select(skill => skill.Name));
    }

    [Fact]
    public void Skills_with_nothing_rare_in_common_do_not_overlap()
    {
        using var vault = Repo()
            .Write("repo/.agents/skills/migrations/SKILL.md", Skill("Reviews database migration scripts."))
            .Write("repo/.agents/skills/rollouts/SKILL.md", Skill("Reviews migration plans before a rollout."))
            .Write("repo/.agents/skills/styles/SKILL.md", Skill("Reviews frontend styles."))
            .Write("repo/.agents/skills/docs/SKILL.md", Skill("Reviews API docs."));

        var codex = For(Check(vault), Harness.Codex);

        // Every skill reviews something, so only the shared migration makes a pair.
        Assert.Equal([("migrations", "rollouts", "migration, reviews")], Pairs(codex));
    }

    [Fact]
    public void Forms_of_one_word_count_as_one_term_shown_as_the_first_skill_writes_it()
    {
        using var vault = Repo()
            .Write("repo/.claude/skills/one/SKILL.md", Skill("Deploying releases."))
            .Write("repo/.claude/skills/two/SKILL.md", Skill("Deploys a release."));

        Assert.Equal([("one", "two", "deploying, releases")], Pairs(For(Check(vault), Harness.ClaudeCode)));
    }

    // Every skill of a plugin shares its namespace, which says nothing about when to use one.
    [Fact]
    public void A_namespace_is_not_a_shared_term()
    {
        using var vault = Repo()
            .Write("repo/.claude-plugin/plugin.json", """{ "name": "shop-toolkit" }""")
            .Write("repo/.agents/skills/tables/SKILL.md", Skill("Tables."))
            .Write("repo/.agents/skills/lint/SKILL.md", Skill("Lint."));

        var codex = For(Check(vault), Harness.Codex);

        Assert.Equal(["shop-toolkit:lint", "shop-toolkit:tables"], codex.Compared.Select(skill => skill.Name));
        Assert.Empty(codex.Pairs);
    }

    // Two skills with one name are a clash skill-name-clash reports. Comparing both would repeat each pair.
    [Fact]
    public void A_name_listed_twice_is_compared_once()
    {
        using var vault = Repo()
            .Write("repo/.agents/skills/deploy/SKILL.md", Skill("Deploys the shop to production."))
            .Write("home/.agents/skills/deploy/SKILL.md", Skill("Deploys the shop to production, the user's copy."))
            .Write("repo/.agents/skills/ship/SKILL.md", Skill("Ships the shop to production."));

        var codex = For(Check(vault), Harness.Codex);

        Assert.Equal(1, codex.Repeats);
        Assert.Equal([("deploy", "ship", "production, shop")], Pairs(codex));
    }

    [Fact]
    public void Built_in_skills_are_left_out_because_their_text_is_not_recorded()
    {
        using var vault = Repo();

        var report = Check(vault);

        Assert.Equal((ClaudeCodeSkills.BuiltIns.Count, 0), (For(report, Harness.ClaudeCode).LeftOut, For(report, Harness.ClaudeCode).Compared.Count));
        Assert.Equal(0, For(report, Harness.Codex).LeftOut);
    }

    // After sync, a vault skill is listed under its name, in place of any copy already there.
    [Fact]
    public void Vault_skills_join_the_harnesses_they_support_in_place_of_a_listed_skill_of_their_name()
    {
        var manifest = TempVault.Manifest("skill", "deploy")
            .Replace("description: Reviews a codebase for decisions an LLM shouldn't own.", "description: Deploys the shop to production.")
            .Replace("use_when: Writing or changing an asset.", "use_when: Releasing the shop.")
            .Replace("  claude-code: experimental\n", "  claude-code: experimental\n  codex: experimental\n")
            .Replace("  generic: full\n", "");
        using var vault = Repo()
            .Write("repo/skills/deploy/asset.yaml", manifest)
            .Write("repo/skills/deploy/skill.md", "# Deploy\n")
            .Write("repo/.claude/skills/deploy/SKILL.md", Skill("An older copy."))
            .Write("repo/.claude/skills/ship/SKILL.md", Skill("Ships the shop to production."));

        var report = Check(vault);

        var claude = For(report, Harness.ClaudeCode);
        Assert.Equal(
            [("deploy", Path.Combine(vault.Root, "repo", "skills", "deploy", "asset.yaml"), true), ("ship", Path.Combine(vault.Root, "repo", ".claude", "skills", "ship", "SKILL.md"), false)],
            claude.Compared.Select(skill => (skill.Name, skill.Path, skill.FromVault)));
        Assert.Equal([("deploy", "ship", "shop, production")], Pairs(claude));
        Assert.Equal(["deploy"], For(report, Harness.Codex).Compared.Select(skill => skill.Name));
    }

    [Fact]
    public void A_vault_skill_is_compared_on_its_description_and_use_when()
    {
        var manifest = TempVault.Manifest("skill", "tables")
            .Replace("description: Reviews a codebase for decisions an LLM shouldn't own.", "description: Formats tables.")
            .Replace("use_when: Writing or changing an asset.", "use_when: The user pastes a spreadsheet.");
        using var vault = Repo()
            .Write("repo/skills/tables/asset.yaml", manifest)
            .Write("repo/skills/tables/skill.md", "# Tables\n")
            .Write("repo/.claude/skills/sheets/SKILL.md", Skill("Cleans up a pasted spreadsheet."));

        Assert.Equal([("sheets", "tables", "pasted, spreadsheet")], Pairs(For(Check(vault), Harness.ClaudeCode)));
    }

    [Fact]
    public void A_missing_folder_is_a_problem()
    {
        using var vault = new TempVault();

        var result = TriggerOverlap.Check(Path.Combine(vault.Root, "nowhere"), TestMachine.For(vault.Root));

        Assert.Null(result.Report);
        Assert.Equal($"The folder {Path.Combine(vault.Root, "nowhere")} does not exist.", result.Problem);
    }
}

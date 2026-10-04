using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class CodexSkillsTests
{
    private static TempVault Repo() => new TempVault().Folder("repo/.git").Folder("home/.codex").Write("repo/src/app.ts", "export const a = 1;\n");

    private static string Skill(string description, string name = "") =>
        $"---\n{(name.Length > 0 ? $"name: {name}\n" : "")}description: {description}\n---\n# Body\n";

    private static Resolution Resolve(TempVault vault, string launch = "repo", string target = "repo/src/app.ts") =>
        CodexModel.Resolve(Path.Combine(vault.Root, launch), Path.Combine(vault.Root, target), TestMachine.For(vault.Root));

    private static (string Name, string Path, string Rule)[] Listed(TempVault vault, Resolution resolution) =>
        [.. resolution.Skills.Select(skill => (skill.Name, TestMachine.Relative(vault.Root, skill.Path!), skill.Rule.Id))];

    private static (string Name, string Rule)[] NotListed(Resolution resolution) =>
        [.. resolution.NotListed.Select(skill => (skill.Name, skill.Rule.Id))];

    // The recordings show bundled skills first, then admin, repo and user skills, by name within each.
    [Fact]
    public void Every_root_lists_at_launch_bundled_then_admin_then_repo_then_user_by_name()
    {
        using var vault = Repo()
            .Write("home/.codex/skills/.system/imagegen/SKILL.md", Skill("Makes images."))
            .Write("codex-admin/skills/audit/SKILL.md", Skill("Audits."))
            .Write("repo/.agents/skills/zeta/SKILL.md", Skill("Zeta."))
            .Write("repo/src/.agents/skills/beta/SKILL.md", Skill("Beta, in the launch directory."))
            .Write("repo/.codex/skills/alpha/SKILL.md", Skill("Alpha, in the project's .codex."))
            .Write("home/.agents/skills/mine/SKILL.md", Skill("Mine."))
            .Write("home/.codex/skills/old/SKILL.md", Skill("Where user skills used to live."))
            .Write(".agents/skills/above/SKILL.md", Skill("Above the project root, so never found."));

        var resolution = Resolve(vault, launch: "repo/src");

        Assert.Equal(
            [
                ("imagegen", "home/.codex/skills/.system/imagegen/SKILL.md", "codex/bundled-skill"),
                ("audit", "codex-admin/skills/audit/SKILL.md", "codex/admin-skill"),
                ("alpha", "repo/.codex/skills/alpha/SKILL.md", "codex/project-skill"),
                ("beta", "repo/src/.agents/skills/beta/SKILL.md", "codex/repo-skill"),
                ("zeta", "repo/.agents/skills/zeta/SKILL.md", "codex/repo-skill"),
                ("mine", "home/.agents/skills/mine/SKILL.md", "codex/user-skill"),
                ("old", "home/.codex/skills/old/SKILL.md", "codex/codex-home-skill"),
            ],
            Listed(vault, resolution));
        Assert.All(resolution.Skills, skill => Assert.Equal(LoadTiming.AtLaunch, skill.Timing));
    }

    // Recorded in the spike: Codex lists the frontmatter name, and both copies of a name.
    [Fact]
    public void A_skill_is_listed_by_its_frontmatter_name_and_two_skills_can_share_one()
    {
        using var vault = Repo()
            .Write("repo/.agents/skills/folder-x/SKILL.md", Skill("Named in its frontmatter.", name: "name-x"))
            .Write("repo/.agents/skills/deploy/SKILL.md", Skill("Repo deploy."))
            .Write("home/.agents/skills/deploy/SKILL.md", Skill("User deploy."));

        Assert.Equal(["deploy", "name-x", "deploy"], Resolve(vault).Skills.Select(skill => skill.Name));
    }

    [Fact]
    public void A_skill_without_a_description_or_frontmatter_or_with_a_long_name_is_skipped()
    {
        using var vault = Repo()
            .Write("repo/.agents/skills/no-description/SKILL.md", "---\nname: no-description\n---\nBody.\n")
            .Write("repo/.agents/skills/no-frontmatter/SKILL.md", "Just a body.\n")
            .Write("repo/.agents/skills/long/SKILL.md", Skill("Named too long.", name: new string('n', 65)))
            .Write("repo/.agents/skills/broken/SKILL.md", "---\ndescription: ok\n bad: indent\n---\nBody.\n");

        var resolution = Resolve(vault);

        Assert.Empty(resolution.Skills);
        Assert.Equal(4, resolution.NotListed.Count(skill => skill.Rule.Id == "codex/skill-invalid"));
    }

    // Codex quotes a value that holds ": " or starts with a bracket, parses once more, and folds every
    // description onto one line. An entry points at its file through a root alias.
    [Fact]
    public void A_description_with_a_colon_is_mended_and_collapsed_to_one_line()
    {
        using var vault = Repo()
            .Write("repo/.agents/skills/aws/SKILL.md", "---\ndescription: Build for AWS: ECS and Lambda\n---\nBody.\n")
            .Write("repo/.agents/skills/tables/SKILL.md", "---\ndescription: >\n  Formats\n  tables.\n---\nBody.\n");

        var skills = Resolve(vault).Skills;

        Assert.Equal("- aws: Build for AWS: ECS and Lambda (file: r0/aws/SKILL.md)".Length, skills.Single(skill => skill.Name == "aws").Chars);
        Assert.Equal("- tables: Formats tables. (file: r0/tables/SKILL.md)".Length, skills.Single(skill => skill.Name == "tables").Chars);
    }

    [Fact]
    public void A_skill_hidden_by_its_policy_or_turned_off_in_config_is_not_listed()
    {
        using var vault = Repo()
            .Write("repo/.agents/skills/manual/SKILL.md", Skill("Only by $manual."))
            .Write("repo/.agents/skills/manual/agents/openai.yaml", "policy:\n  allow_implicit_invocation: false\n")
            .Write("repo/.agents/skills/chat/SKILL.md", Skill("For ChatGPT only."))
            .Write("repo/.agents/skills/chat/agents/openai.yaml", "policy:\n  products: [chatgpt]\n")
            .Write("repo/.agents/skills/both/SKILL.md", Skill("For Codex too."))
            .Write("repo/.agents/skills/both/agents/openai.yaml", "policy:\n  products: [chatgpt, codex]\n")
            .Write("repo/.agents/skills/off-by-name/SKILL.md", Skill("Turned off by name."))
            .Write("repo/.agents/skills/off-by-path/SKILL.md", Skill("Turned off by path."))
            .Write("home/.codex/skills/.system/imagegen/SKILL.md", Skill("Makes images."));
        var offPath = Path.Combine(vault.Root, "repo", ".agents", "skills", "off-by-path", "SKILL.md").Replace('\\', '/');
        vault.Write(
            "home/.codex/config.toml",
            $"[skills.bundled]\nenabled = false\n\n[[skills.config]]\nname = \"off-by-name\"\nenabled = false\n\n[[skills.config]]\npath = \"{offPath}\"\nenabled = false\n");

        var resolution = Resolve(vault);

        Assert.Equal(["both"], resolution.Skills.Select(skill => skill.Name));
        Assert.Equal(
            [
                ("imagegen", "codex/bundled-off"),
                ("chat", "codex/other-product"),
                ("manual", "codex/implicit-invocation-off"),
                ("off-by-name", "codex/skill-disabled"),
                ("off-by-path", "codex/skill-disabled"),
            ],
            NotListed(resolution));
    }

    // Recorded in the spike and in codex-skills: a plugin manifest at or above a skills root names its skills
    // plugin:skill, and one inside the root doesn't count.
    [Fact]
    public void A_skill_under_a_plugin_manifest_above_its_root_is_named_after_the_plugin()
    {
        using var vault = Repo()
            .Write("repo/.claude-plugin/plugin.json", """{ "name": "plugrepo" }""")
            .Write("repo/.agents/skills/inside/SKILL.md", Skill("Inside a plugin repo."))
            .Write("home/.agents/skills/tools/.codex-plugin/plugin.json", """{ "name": "toolkit" }""")
            .Write("home/.agents/skills/tools/lint/SKILL.md", Skill("Under a manifest inside the root."));

        Assert.Equal(["plugrepo:inside", "lint"], Resolve(vault).Skills.Select(skill => skill.Name));
    }

    [Fact]
    public void Skills_nest_up_to_six_levels_below_a_root_and_hidden_folders_are_skipped()
    {
        using var vault = Repo()
            .Write("repo/.agents/skills/group/deploy/SKILL.md", Skill("Nested once."))
            .Write("repo/.agents/skills/a/b/c/d/deep/SKILL.md", Skill("Six levels down."))
            .Write("repo/.agents/skills/a/b/c/d/e/too-deep/SKILL.md", Skill("Seven levels down."))
            .Write("repo/.agents/skills/.hidden/secret/SKILL.md", Skill("In a hidden folder."));

        Assert.Equal(["deep", "deploy"], Resolve(vault).Skills.Select(skill => skill.Name));
    }

    // Recorded in the spike: an untrusted project's .codex/skills is listed, which the docs say it isn't.
    [Fact]
    public void An_untrusted_projects_codex_skills_are_still_listed()
    {
        using var vault = Repo()
            .Write("repo/.codex/skills/local/SKILL.md", Skill("Local."));
        vault.Write("home/.codex/config.toml", $"[projects.'{Path.Combine(vault.Root, "repo")}']\ntrust_level = \"untrusted\"\n");

        Assert.Equal(["local"], Resolve(vault).Skills.Select(skill => skill.Name));
    }

    [Fact]
    public void A_description_past_the_cap_is_cut_with_three_dots_and_the_listing_is_counted_in_tokens()
    {
        using var vault = Repo().Write("repo/.agents/skills/long/SKILL.md", Skill(new string('x', 1500)));

        var resolution = Resolve(vault);

        Assert.True(resolution.Skills.Single().Cut);
        Assert.Equal(new string('x', 1021) + "...", resolution.Skills.Single().Text);
        Assert.Equal("tokens", resolution.Listing!.Unit);
        Assert.Equal(5440, resolution.Listing.Budget);
        Assert.InRange(resolution.Listing.Size, 1024 / 4, 1200 / 4);
    }
}

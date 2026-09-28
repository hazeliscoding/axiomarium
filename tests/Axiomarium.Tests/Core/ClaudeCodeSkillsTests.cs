using System.Text.Json.Nodes;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class ClaudeCodeSkillsTests
{
    private static readonly string[] BuiltIns = [.. ClaudeCodeSkills.BuiltIns.Select(skill => skill.Name)];

    private static TempVault Repo() => new TempVault().Folder("repo/.git").Write("repo/src/app.ts", "export const a = 1;\n");

    private static string Skill(string description, string extra = "") => $"---\ndescription: {description}\n{extra}---\n# Body\n\nSteps.\n";

    private static Resolution Resolve(TempVault vault, string launch = "repo", string target = "repo/src/app.ts") =>
        ClaudeCodeModel.Resolve(Path.Combine(vault.Root, launch), Path.Combine(vault.Root, target), TestMachine.For(vault.Root));

    private static (string Name, string? Path, string Rule)[] Listed(TempVault vault, Resolution resolution, LoadTiming timing) =>
        [.. resolution.Skills
            .Where(skill => skill.Timing == timing && skill.Path is not null)
            .Select(skill => (skill.Name, (string?)TestMachine.Relative(vault.Root, skill.Path!), skill.Rule.Id))];

    private static (string Name, string Rule)[] NotListed(Resolution resolution) =>
        [.. resolution.NotListed.Select(skill => (skill.Name, skill.Rule.Id))];

    [Fact]
    public void At_launch_managed_then_personal_then_project_skills_from_the_launch_directory_up_then_commands_then_built_ins()
    {
        using var vault = Repo()
            .Write("managed/.claude/skills/audit/SKILL.md", Skill("Audits."))
            .Write("home/.claude/skills/review/SKILL.md", Skill("Reviews."))
            .Write("home/.claude/skills/checklist/SKILL.md", Skill("Checks."))
            .Write("repo/.claude/skills/deploy/SKILL.md", Skill("Deploys."))
            .Write("repo/src/.claude/skills/api/SKILL.md", Skill("API style."))
            .Write("repo/.claude/commands/tools/lint.md", "---\ndescription: Lints.\n---\nLint it.\n")
            .Write(".claude/skills/above/SKILL.md", Skill("Above the repo, so never found."));

        var resolution = Resolve(vault, launch: "repo/src");

        Assert.Equal(
            [
                ("audit", "managed/.claude/skills/audit/SKILL.md", "claude-code/managed-skill"),
                ("checklist", "home/.claude/skills/checklist/SKILL.md", "claude-code/personal-skill"),
                ("review", "home/.claude/skills/review/SKILL.md", "claude-code/personal-skill"),
                ("api", "repo/src/.claude/skills/api/SKILL.md", "claude-code/project-skill"),
                ("deploy", "repo/.claude/skills/deploy/SKILL.md", "claude-code/project-skill"),
                ("tools:lint", "repo/.claude/commands/tools/lint.md", "claude-code/command"),
            ],
            Listed(vault, resolution, LoadTiming.AtLaunch));
        Assert.Equal(BuiltIns, resolution.Skills.Where(skill => skill.Path is null).Select(skill => skill.Name));
        Assert.All(resolution.Skills.Where(skill => skill.Path is null), skill => Assert.Equal("claude-code/built-in-skill", skill.Rule.Id));
    }

    [Fact]
    public void Personal_beats_project_a_skill_beats_a_command_and_your_skill_replaces_a_built_in()
    {
        using var vault = Repo()
            .Write("home/.claude/skills/deploy/SKILL.md", Skill("Personal deploy."))
            .Write("repo/.claude/skills/deploy/SKILL.md", Skill("Project deploy."))
            .Write("repo/.claude/skills/lint/SKILL.md", Skill("Lint skill."))
            .Write("repo/.claude/commands/lint.md", "Lint command.\n")
            .Write("repo/.claude/skills/init/SKILL.md", Skill("Our own init."));

        var resolution = Resolve(vault);

        Assert.Equal(
            [("deploy", "home/.claude/skills/deploy/SKILL.md"), ("init", "repo/.claude/skills/init/SKILL.md"), ("lint", "repo/.claude/skills/lint/SKILL.md")],
            Listed(vault, resolution, LoadTiming.AtLaunch).Select(skill => (skill.Name, skill.Path!)));
        Assert.Equal([("deploy", "claude-code/skill-shadowed"), ("lint", "claude-code/skill-shadowed"), ("init", "claude-code/skill-shadowed")], NotListed(resolution));
        Assert.Equal(["repo/.claude/skills/deploy/SKILL.md", "repo/.claude/commands/lint.md", null], resolution.NotListed.Select(skill => skill.Path is null ? null : TestMachine.Relative(vault.Root, skill.Path)));
    }

    [Fact]
    public void A_skill_the_model_may_not_invoke_or_a_setting_hides_is_not_listed()
    {
        using var vault = Repo()
            .Write("repo/.claude/skills/manual/SKILL.md", Skill("Only by hand.", "disable-model-invocation: yes\n"))
            .Write("repo/.claude/skills/quiet/SKILL.md", Skill("Hidden by a setting."))
            .Write("repo/.claude/skills/short/SKILL.md", Skill("Listed by name."))
            .Write("repo/.claude/skills/typed/SKILL.md", Skill("Typed by the user only.", "user-invocable: false\n"))
            .Write("home/.claude/settings.json", """{ "skillOverrides": { "quiet": "off", "short": "name-only", "loop": "user-invocable-only" } }""");

        var resolution = Resolve(vault);

        Assert.Equal(
            [("manual", "claude-code/model-invocation-off"), ("quiet", "claude-code/skill-override"), ("loop", "claude-code/skill-override")],
            NotListed(resolution));
        var listed = resolution.Skills.Single(skill => skill.Name == "short");
        Assert.True(listed.NameOnly);
        Assert.Equal("- short".Length, listed.Chars);
        Assert.Contains(resolution.Skills, skill => skill.Name == "typed");
    }

    [Fact]
    public void A_skill_with_paths_joins_when_the_file_matches_them_from_the_launch_directory()
    {
        using var vault = Repo()
            .Write("repo/.claude/skills/typescript/SKILL.md", Skill("TypeScript.", "paths:\n  - \"src/**/*.ts\"\n"))
            .Write("repo/.claude/skills/docs/SKILL.md", Skill("Docs.", "paths: docs/**\n"));

        var resolution = Resolve(vault);

        Assert.Empty(Listed(vault, resolution, LoadTiming.AtLaunch));
        var typescript = Assert.Single(resolution.Skills, skill => skill.Timing == LoadTiming.OnRead);
        Assert.Equal(("typescript", "claude-code/paths-skill"), (typescript.Name, typescript.Rule.Id));
        Assert.Equal(["src/**/*.ts"], typescript.Patterns!);
        Assert.Equal([("docs", "claude-code/paths-skill-no-match")], NotListed(resolution));
    }

    [Fact]
    public void A_nested_skill_joins_when_the_file_is_below_its_folder_and_says_where_it_applies()
    {
        using var vault = Repo()
            .Write("repo/.claude/skills/deploy/SKILL.md", Skill("Root deploy."))
            .Write("repo/src/.claude/skills/deploy/SKILL.md", Skill("Src deploy."))
            .Write("repo/src/.claude/skills/api/SKILL.md", Skill("API style."));

        var resolution = Resolve(vault);

        Assert.Equal(
            [("api", "repo/src/.claude/skills/api/SKILL.md", "claude-code/nested-skill"), ("src:deploy", "repo/src/.claude/skills/deploy/SKILL.md", "claude-code/nested-skill")],
            Listed(vault, resolution, LoadTiming.OnRead));
        var api = resolution.Skills.Single(skill => skill.Name == "api");
        Assert.Equal("- api: API style. (from src/.claude/skills — applies when working on files under src/)".Length, api.Chars);
        var deploy = resolution.Skills.Single(skill => skill.Name == "src:deploy");
        Assert.Equal(
            "- src:deploy: Src deploy. (scoped to src/ — use this instead of the unscoped \"deploy\" skill when the files being changed are under src/)".Length,
            deploy.Chars);
    }

    // The listing shows description and when_to_use. A skill whose frontmatter doesn't parse keeps only its
    // first body line, unless quoting its values mends it, as the spike and the recordings show on 2.1.284.
    [Fact]
    public void An_entry_is_the_description_and_when_to_use_or_the_first_body_line()
    {
        using var vault = Repo()
            .Write("repo/.claude/skills/tables/SKILL.md", Skill("Formats tables", "when_to_use: when the user pastes a table\n"))
            .Write("repo/.claude/skills/broken/SKILL.md", "---\ndescription: [unclosed\n  bad: - indent\n---\nFirst body line\n\nMore.\n")
            .Write("repo/.claude/skills/mended/SKILL.md", "---\ndescription: Mended by quoting.\nextra: [unclosed\n---\nFirst body line\n")
            .Write("repo/.claude/skills/bare/SKILL.md", "\nJust a body line.\n");

        var resolution = Resolve(vault);

        int Chars(string name) => resolution.Skills.Single(skill => skill.Name == name).Chars;
        Assert.Equal("- tables: Formats tables - when the user pastes a table".Length, Chars("tables"));
        Assert.Equal("- broken: First body line".Length, Chars("broken"));
        Assert.Equal("- mended: Mended by quoting.".Length, Chars("mended"));
        Assert.Equal("- bare: Just a body line.".Length, Chars("bare"));
    }

    // Recorded in claude-code-skills: a pattern without a slash matches at any depth, as in a .gitignore.
    [Fact]
    public void A_skills_paths_match_the_way_a_gitignore_line_does()
    {
        using var vault = Repo()
            .Write("repo/src/api/orders.ts", "export const orders = [];\n")
            .Write("repo/.claude/skills/star/SKILL.md", Skill("Any TypeScript file.", "paths: \"*.ts\"\n"))
            .Write("repo/.claude/skills/folder/SKILL.md", Skill("Anything in src.", "paths: src\n"))
            .Write("repo/.claude/skills/api/SKILL.md", Skill("Anything in an api folder.", "paths: \"api/**\"\n"))
            .Write("repo/.claude/skills/anchored/SKILL.md", Skill("Only a top-level file.", "paths: /orders.ts\n"))
            .Write("repo/.claude/skills/tail/SKILL.md", Skill("A slash inside anchors it.", "paths: api/orders.ts\n"));

        var resolution = Resolve(vault, target: "repo/src/api/orders.ts");

        Assert.Equal(["api", "folder", "star"], resolution.Skills.Where(skill => skill.Timing == LoadTiming.OnRead).Select(skill => skill.Name));
        Assert.Equal([("anchored", "claude-code/paths-skill-no-match"), ("tail", "claude-code/paths-skill-no-match")], NotListed(resolution));
    }

    [Fact]
    public void A_description_over_the_cap_is_cut_with_an_ellipsis()
    {
        using var vault = Repo().Write("repo/.claude/skills/long/SKILL.md", Skill(new string('x', 2000)));

        var skill = Resolve(vault).Skills.Single(skill => skill.Name == "long");

        Assert.True(skill.Cut);
        Assert.Equal("- long: ".Length + 1536, skill.Chars);
    }

    [Fact]
    public void The_listing_counts_every_entry_at_launch_and_a_newline_between_them_against_one_percent_of_the_window()
    {
        using var vault = Repo()
            .Write("repo/.claude/skills/one/SKILL.md", Skill(new string('a', 1500)))
            .Write("repo/.claude/skills/two/SKILL.md", Skill(new string('b', 1500)));

        var resolution = Resolve(vault);

        var atLaunch = resolution.Skills.Where(skill => skill.Timing == LoadTiming.AtLaunch).ToList();
        Assert.Equal(atLaunch.Sum(skill => skill.Chars) + atLaunch.Count - 1, resolution.Listing!.Size);
        Assert.Equal(8000, resolution.Listing.Budget);
        Assert.True(resolution.Listing.OverBudget);
    }

    // Plugin state names absolute paths, so it's written once the vault's folder is known.
    private static TempVault WithPlugins(TempVault vault, JsonObject installed, JsonObject marketplaces, JsonObject enabled)
    {
        vault.Write("home/.claude/plugins/installed_plugins.json", new JsonObject { ["version"] = 2, ["plugins"] = installed }.ToJsonString());
        vault.Write("home/.claude/plugins/known_marketplaces.json", marketplaces.ToJsonString());
        return vault.Write("home/.claude/settings.json", new JsonObject { ["enabledPlugins"] = enabled }.ToJsonString());
    }

    [Fact]
    public void An_enabled_plugins_skills_and_commands_are_listed_under_its_name_after_the_commands_and_before_the_built_ins()
    {
        using var vault = Repo()
            .Write("repo/.claude/commands/lint.md", "Lint.\n")
            .Write("home/.claude/plugins/cache/market/tools/1.0.0/skills/review/SKILL.md", Skill("Reviews."))
            .Write("home/.claude/plugins/cache/market/tools/1.0.0/skills/fancy-folder/SKILL.md", "---\nname: fancy\ndescription: Named in its frontmatter.\n---\nBody.\n")
            .Write("home/.claude/plugins/cache/market/tools/1.0.0/skills/bare/SKILL.md", "Only a body.\n")
            .Write("home/.claude/plugins/cache/market/tools/1.0.0/commands/ship.md", "---\ndescription: Ships.\n---\nShip it.\n")
            .Write("home/.claude/plugins/cache/other/off/1.0.0/skills/never/SKILL.md", Skill("A plugin that's turned off."));
        var cache = Path.Combine(vault.Root, "home", ".claude", "plugins", "cache");
        WithPlugins(
            vault,
            new JsonObject
            {
                ["tools@market"] = new JsonArray(new JsonObject { ["scope"] = "user", ["installPath"] = Path.Combine(cache, "market", "tools", "1.0.0") }),
                ["off@other"] = new JsonArray(new JsonObject { ["scope"] = "user", ["installPath"] = Path.Combine(cache, "other", "off", "1.0.0") }),
            },
            new JsonObject { ["market"] = new JsonObject { ["source"] = new JsonObject { ["source"] = "github", ["repo"] = "someone/market" } } },
            new JsonObject { ["tools@market"] = true, ["off@other"] = false });
        vault.Write("repo/.claude/settings.json", """{ "skillOverrides": { "tools:review": "off" } }""");

        var resolution = Resolve(vault);

        Assert.Equal(
            ["lint", "tools:fancy", "tools:review", "tools:ship", .. BuiltIns],
            resolution.Skills.Select(skill => skill.Name));
        Assert.Equal("claude-code/plugin-skill", resolution.Skills.Single(skill => skill.Name == "tools:review").Rule.Id);
        Assert.Equal([("tools:bare", "claude-code/plugin-skill-no-description")], NotListed(resolution));
    }

    [Fact]
    public void A_plugin_from_a_marketplace_in_a_local_folder_loads_in_place()
    {
        using var vault = Repo()
            .Write("market/.claude-plugin/marketplace.json", """{ "name": "local", "plugins": [ { "name": "dev", "source": "./dev" } ] }""")
            .Write("market/dev/skills/helper/SKILL.md", Skill("Helps."));
        WithPlugins(
            vault,
            new JsonObject { ["dev@local"] = new JsonArray(new JsonObject { ["scope"] = "user", ["installPath"] = Path.Combine(vault.Root, "home", ".claude", "plugins", "cache", "local", "dev", "1.0.0") }) },
            new JsonObject { ["local"] = new JsonObject { ["source"] = new JsonObject { ["source"] = "directory", ["path"] = Path.Combine(vault.Root, "market") } } },
            new JsonObject { ["dev@local"] = true });

        var helper = Resolve(vault).Skills.Single(skill => skill.Name == "dev:helper");

        Assert.Equal("market/dev/skills/helper/SKILL.md", TestMachine.Relative(vault.Root, helper.Path!));
    }

    [Fact]
    public void Skills_synced_from_claude_ai_come_after_the_built_ins_under_their_full_name_unless_the_sync_is_off()
    {
        using var vault = Repo()
            .Write("home/.claude/skills/synced/account/pdf/SKILL.md", Skill("PDFs."))
            .Write("home/.claude/skills/synced/.bucket-account/stale/SKILL.md", Skill("Not a skill folder."));

        Assert.Equal("anthropic-skills:pdf", Resolve(vault).Skills.Last().Name);

        vault.Write("home/.claude/settings.json", """{ "syncClaudeAiSkills": false }""");
        Assert.DoesNotContain(Resolve(vault).Skills, skill => skill.Name.StartsWith("anthropic-skills:", StringComparison.Ordinal));
    }

    [Fact]
    public void Settings_can_turn_the_built_ins_off_and_change_the_budget_and_the_cap()
    {
        using var vault = Repo()
            .Write("repo/.claude/skills/long/SKILL.md", Skill(new string('x', 300)))
            .Write("home/.claude/settings.json", """{ "disableBundledSkills": true, "skillListingBudgetFraction": 0.02, "skillListingMaxDescChars": 100 }""");

        var resolution = Resolve(vault);

        Assert.DoesNotContain(resolution.Skills, skill => skill.Path is null);
        Assert.Equal(BuiltIns.Length, resolution.NotListed.Count(skill => skill.Rule.Id == "claude-code/built-ins-off"));
        Assert.Equal(16000, resolution.Listing!.Budget);
        Assert.Equal("- long: ".Length + 100, resolution.Skills.Single().Chars);
    }
}

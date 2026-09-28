using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class ClaudeCodeModelTests
{
    private static Resolution Resolve(TempVault vault, string launch = "repo", string target = "repo/src/api/orders.cs") =>
        ClaudeCodeModel.Resolve(Path.Combine(vault.Root, launch), Path.Combine(vault.Root, target), TestMachine.For(vault.Root));

    private static (string, InstructionScope, string)[] Loaded(TempVault vault, Resolution resolution, LoadTiming timing) =>
        [.. resolution.Loaded.Where(item => item.Timing == timing).Select(item => (TestMachine.Relative(vault.Root, item.Path), item.Scope, item.Rule.Id))];

    [Fact]
    public void At_launch_managed_then_user_then_each_directory_from_the_root_down()
    {
        using var vault = new TempVault()
            .Write("managed/CLAUDE.md", "managed\n")
            .Write("home/.claude/CLAUDE.md", "user\n")
            .Write("home/.claude/rules/style.md", "style\n")
            .Write("CLAUDE.md", "above the repo\n")
            .Write("repo/CLAUDE.local.md", "root local\n")
            .Write("repo/.claude/rules/b.md", "rule b\n")
            .Write("repo/.claude/rules/a.md", "rule a\n")
            .Write("repo/.claude/CLAUDE.md", "root hidden\n")
            .Write("repo/CLAUDE.md", "root\n")
            .Write("repo/src/CLAUDE.md", "src\n");

        var resolution = Resolve(vault, launch: "repo/src");

        Assert.Equal(
            [
                ("managed/CLAUDE.md", InstructionScope.Managed, "claude-code/managed-memory"),
                ("home/.claude/CLAUDE.md", InstructionScope.User, "claude-code/user-memory"),
                ("home/.claude/rules/style.md", InstructionScope.User, "claude-code/user-rule"),
                ("CLAUDE.md", InstructionScope.Project, "claude-code/ancestor-memory"),
                ("repo/CLAUDE.md", InstructionScope.Project, "claude-code/ancestor-memory"),
                ("repo/.claude/CLAUDE.md", InstructionScope.Project, "claude-code/ancestor-memory"),
                ("repo/.claude/rules/a.md", InstructionScope.Project, "claude-code/ancestor-rule"),
                ("repo/.claude/rules/b.md", InstructionScope.Project, "claude-code/ancestor-rule"),
                ("repo/CLAUDE.local.md", InstructionScope.Local, "claude-code/local-memory"),
                ("repo/src/CLAUDE.md", InstructionScope.Project, "claude-code/ancestor-memory"),
            ],
            Loaded(vault, resolution, LoadTiming.AtLaunch));
    }

    [Fact]
    public void Claude_files_below_the_launch_directory_load_when_the_target_is_read()
    {
        using var vault = new TempVault()
            .Write("repo/CLAUDE.md", "root\n")
            .Write("repo/src/CLAUDE.md", "src\n")
            .Write("repo/src/api/CLAUDE.local.md", "api local\n")
            .Write("repo/src/api/CLAUDE.md", "api\n")
            .Write("repo/web/CLAUDE.md", "not on the target's path\n");

        var resolution = Resolve(vault);

        Assert.Equal([("repo/CLAUDE.md", InstructionScope.Project, "claude-code/ancestor-memory")], Loaded(vault, resolution, LoadTiming.AtLaunch));
        Assert.Equal(
            [
                ("repo/src/CLAUDE.md", InstructionScope.Project, "claude-code/nested-memory"),
                ("repo/src/api/CLAUDE.md", InstructionScope.Project, "claude-code/nested-memory"),
                ("repo/src/api/CLAUDE.local.md", InstructionScope.Local, "claude-code/nested-memory"),
            ],
            Loaded(vault, resolution, LoadTiming.OnRead));
    }

    // What the recordings show: trimmed at launch, as-is when read, and frontmatter always removed.
    [Fact]
    public void Bytes_are_what_the_model_sees()
    {
        using var vault = new TempVault()
            .Write("repo/CLAUDE.md", "root\n\n")
            .Write("repo/.claude/rules/always.md", "---\ndescription: x\n---\nalways\n")
            .Write("repo/src/api/CLAUDE.md", "api\n");

        var resolution = Resolve(vault);

        Assert.Equal([4, 6, 4], resolution.Loaded.Select(item => item.Bytes));
    }

    // Claude Code's walk from the root passes through the home folder, where .claude/CLAUDE.md is also the
    // user memory. It can't be recorded with a fake home, so this follows the docs: one file, loaded once.
    [Fact]
    public void The_user_memory_loads_once_when_the_walk_passes_through_the_home_folder()
    {
        using var vault = new TempVault().Write("home/.claude/CLAUDE.md", "user\n").Write("home/repo/CLAUDE.md", "root\n");

        var resolution = Resolve(vault, launch: "home/repo", target: "home/repo/app.cs");

        Assert.Equal(
            [("home/.claude/CLAUDE.md", InstructionScope.User, "claude-code/user-memory"), ("home/repo/CLAUDE.md", InstructionScope.Project, "claude-code/ancestor-memory")],
            Loaded(vault, resolution, LoadTiming.AtLaunch));
    }

    private static (string, string, string?)[] Dropped(TempVault vault, Resolution resolution) =>
        [.. resolution.Dropped.Select(item => (TestMachine.Relative(vault.Root, item.Path), item.Rule.Id, item.Via is { } via ? $"{TestMachine.Relative(vault.Root, via.File)}:{via.Line}" : null))];

    [Fact]
    public void Imports_load_depth_first_right_after_the_file_that_imports_them()
    {
        using var vault = new TempVault()
            .Write("repo/CLAUDE.md", "root\nStart with @docs/a.md then @docs/c.md\n")
            .Write("repo/docs/a.md", "a\n@b.md\n")
            .Write("repo/docs/b.md", "b\n")
            .Write("repo/docs/c.md", "c\n")
            .Write("repo/CLAUDE.local.md", "local @docs/notes.md\n")
            .Write("repo/docs/notes.md", "notes\n");

        var resolution = Resolve(vault);

        Assert.Equal(
            [
                ("repo/CLAUDE.md", InstructionScope.Project, "claude-code/ancestor-memory"),
                ("repo/docs/a.md", InstructionScope.Project, "claude-code/import"),
                ("repo/docs/b.md", InstructionScope.Project, "claude-code/import"),
                ("repo/docs/c.md", InstructionScope.Project, "claude-code/import"),
                ("repo/CLAUDE.local.md", InstructionScope.Local, "claude-code/local-memory"),
                ("repo/docs/notes.md", InstructionScope.Local, "claude-code/import"),
            ],
            Loaded(vault, resolution, LoadTiming.AtLaunch));
        Assert.Equal("repo/CLAUDE.md:2", resolution.Loaded[1].Via is { } via ? $"{TestMachine.Relative(vault.Root, via.File)}:{via.Line}" : null);
    }

    [Fact]
    public void Code_spans_and_fenced_blocks_hold_no_imports()
    {
        using var vault = new TempVault()
            .Write("repo/CLAUDE.md", "root `@docs/span.md`\n\n```\n@docs/fenced.md\n```\n~~~\n@docs/tilde.md\n~~~\n")
            .Write("repo/docs/span.md", "span\n")
            .Write("repo/docs/fenced.md", "fenced\n")
            .Write("repo/docs/tilde.md", "tilde\n");

        Assert.Single(Resolve(vault).Loaded);
    }

    // Trailing punctuation is part of the path, so "@docs/guide.md." looks for "docs/guide.md.".
    [Fact]
    public void A_missing_import_is_dropped_with_where_it_was_imported()
    {
        using var vault = new TempVault().Write("repo/CLAUDE.md", "root\nRead @docs/guide.md.\nAnd @docs/gone.md\n").Write("repo/docs/guide.md", "guide\n");

        Assert.Equal(
            [("repo/docs/guide.md.", "claude-code/missing-import", "repo/CLAUDE.md:2"), ("repo/docs/gone.md", "claude-code/missing-import", "repo/CLAUDE.md:3")],
            Dropped(vault, Resolve(vault)));
    }

    [Fact]
    public void Imports_stop_after_four_hops()
    {
        using var vault = new TempVault().Write("repo/CLAUDE.md", "@d1.md\n");
        for (var i = 1; i <= 5; i++)
        {
            vault.Write($"repo/d{i}.md", $"d{i} @d{i + 1}.md\n");
        }

        vault.Write("repo/d6.md", "d6\n");
        var resolution = Resolve(vault);

        Assert.Equal(["repo/CLAUDE.md", "repo/d1.md", "repo/d2.md", "repo/d3.md", "repo/d4.md"], resolution.Loaded.Select(item => TestMachine.Relative(vault.Root, item.Path)));
        Assert.Equal([("repo/d5.md", "claude-code/import-too-deep", "repo/d4.md:1")], Dropped(vault, resolution));
    }

    [Fact]
    public void A_cycle_loads_each_file_once()
    {
        using var vault = new TempVault().Write("repo/CLAUDE.md", "@a.md\n").Write("repo/a.md", "a @b.md\n").Write("repo/b.md", "b @a.md\n");

        Assert.Equal(3, Resolve(vault).Loaded.Count);
    }

    // Recorded: launched in src/app, the root CLAUDE.md's import of docs/guide.md didn't load.
    [Fact]
    public void A_project_import_outside_the_launch_directory_waits_for_approval()
    {
        using var vault = new TempVault()
            .Write("repo/CLAUDE.md", "@docs/guide.md\n")
            .Write("repo/docs/guide.md", "guide\n")
            .Write("repo/src/CLAUDE.md", "@notes.md\n")
            .Write("repo/src/notes.md", "notes\n");

        var resolution = Resolve(vault, launch: "repo/src", target: "repo/src/app.cs");

        Assert.Equal(["repo/CLAUDE.md", "repo/src/CLAUDE.md", "repo/src/notes.md"], resolution.Loaded.Select(item => TestMachine.Relative(vault.Root, item.Path)));
        Assert.Equal([("repo/docs/guide.md", "claude-code/external-import", "repo/CLAUDE.md:1")], Dropped(vault, resolution));
    }

    [Fact]
    public void The_user_memory_may_import_from_anywhere_including_home()
    {
        using var vault = new TempVault().Write("home/.claude/CLAUDE.md", "@~/notes/mine.md\n").Write("home/notes/mine.md", "mine\n");

        Assert.Equal(
            [("home/.claude/CLAUDE.md", InstructionScope.User, "claude-code/user-memory"), ("home/notes/mine.md", InstructionScope.User, "claude-code/import")],
            Loaded(vault, Resolve(vault), LoadTiming.AtLaunch));
    }

    private static string PathRule(string paths) => $"---\npaths:\n  - \"{paths}\"\n---\nrule\n";

    // Recorded in path-rules and nested-rules: user rules, then the folders below the launch directory,
    // then the path rules of the launch directory and above, each matched from its own folder.
    [Fact]
    public void Path_rules_load_on_read_matched_from_their_own_folder()
    {
        using var vault = new TempVault()
            .Write("home/.claude/rules/user.md", PathRule("src/api/**"))
            .Write("repo/.claude/rules/root.md", PathRule("src/api/**"))
            .Write("repo/.claude/rules/other.md", PathRule("src/web/**"))
            .Write("repo/src/CLAUDE.md", "src\n")
            .Write("repo/src/.claude/rules/relative.md", PathRule("api/**"))
            .Write("repo/src/.claude/rules/wrong-base.md", PathRule("src/api/**"))
            .Write("repo/src/api/.claude/rules/plain.md", "plain\n");

        var resolution = Resolve(vault);

        Assert.Equal(
            [
                ("home/.claude/rules/user.md", InstructionScope.User, "claude-code/path-rule"),
                ("repo/src/CLAUDE.md", InstructionScope.Project, "claude-code/nested-memory"),
                ("repo/src/.claude/rules/relative.md", InstructionScope.Project, "claude-code/path-rule"),
                ("repo/src/api/.claude/rules/plain.md", InstructionScope.Project, "claude-code/nested-rule"),
                ("repo/.claude/rules/root.md", InstructionScope.Project, "claude-code/path-rule"),
            ],
            Loaded(vault, resolution, LoadTiming.OnRead));
        Assert.Equal(
            [("repo/src/.claude/rules/wrong-base.md", "claude-code/path-rule-no-match", null), ("repo/.claude/rules/other.md", "claude-code/path-rule-no-match", null)],
            Dropped(vault, resolution));
    }

    [Fact]
    public void A_comma_separated_paths_string_and_an_invalid_pattern_next_to_a_valid_one()
    {
        using var vault = new TempVault()
            .Write("repo/.claude/rules/comma.md", "---\npaths: \"docs/**, src/api/*.cs\"\n---\ncomma\n")
            .Write("repo/.claude/rules/mixed.md", "---\npaths:\n  - \"src/[api/**\"\n  - \"src/api/*.cs\"\n---\nmixed\n");

        Assert.Equal(["repo/.claude/rules/comma.md", "repo/.claude/rules/mixed.md"], Resolve(vault).Loaded.Select(item => TestMachine.Relative(vault.Root, item.Path)));
    }

    // Recorded in bad-frontmatter: as the docs say, a rule whose frontmatter doesn't parse loads for every file.
    [Fact]
    public void A_rule_whose_frontmatter_does_not_parse_loads_at_launch_for_every_file()
    {
        using var vault = new TempVault().Write("repo/.claude/rules/broken.md", "---\npaths:\n  - \"src/api/**\"\n bad: indent\n---\nbroken\n");

        var resolution = Resolve(vault);

        Assert.Equal(
            [("repo/.claude/rules/broken.md", InstructionScope.Project, "claude-code/invalid-frontmatter")],
            Loaded(vault, resolution, LoadTiming.AtLaunch));
        Assert.Empty(resolution.Dropped);
    }

    // Recorded in bad-frontmatter: Claude Code quotes a value that looks like YAML syntax and parses again,
    // so an unclosed bracket becomes part of a pattern that matches nothing.
    [Fact]
    public void A_value_claude_code_can_quote_becomes_a_pattern_that_matches_nothing()
    {
        using var vault = new TempVault().Write("repo/.claude/rules/unclosed.md", "---\npaths: [src/api/**\n---\nunclosed\n");

        var resolution = Resolve(vault);

        Assert.Empty(resolution.Loaded);
        Assert.Equal([("repo/.claude/rules/unclosed.md", "claude-code/path-rule-no-match", null)], Dropped(vault, resolution));
    }

    // Recorded in path-rules: a pattern without a slash matches at any depth, one with a slash inside is
    // anchored, and a trailing /** matches a folder at any depth, as a trailing slash does.
    [Fact]
    public void A_rules_paths_match_the_way_a_gitignore_line_does()
    {
        using var vault = new TempVault()
            .Write("repo/.claude/rules/any-depth.md", "---\npaths: orders.cs\n---\nany depth\n")
            .Write("repo/.claude/rules/folder.md", "---\npaths: \"api/**\"\n---\nfolder\n")
            .Write("repo/.claude/rules/anchored.md", "---\npaths: api/orders.cs\n---\nanchored\n");

        var resolution = Resolve(vault);

        Assert.Equal(["repo/.claude/rules/any-depth.md", "repo/.claude/rules/folder.md"], resolution.Loaded.Select(item => TestMachine.Relative(vault.Root, item.Path)));
        Assert.Equal([("repo/.claude/rules/anchored.md", "claude-code/path-rule-no-match", null)], Dropped(vault, resolution));
    }

    // Recorded in bad-frontmatter: Claude Code's YAML reader accepts tab indentation.
    [Fact]
    public void Tab_indented_frontmatter_still_parses()
    {
        using var vault = new TempVault().Write("repo/.claude/rules/tabs.md", "---\npaths:\n\t- src/api/**\n---\ntabs\n");

        Assert.Equal("claude-code/path-rule", Assert.Single(Resolve(vault).Loaded).Rule.Id);
    }

    // A rule's paths share a budget of 1,000 expanded patterns. Past it, a pattern's braces are literal.
    [Fact]
    public void A_pattern_past_the_brace_expansion_budget_matches_nothing()
    {
        var tenPairs = string.Concat(Enumerable.Repeat("{a,b}", 10));
        using var vault = new TempVault()
            .Write("repo/.claude/rules/huge.md", PathRule($"src/{tenPairs}/**"))
            .Write($"repo/src/{new string('a', 10)}/orders.cs", "class Orders { }\n");

        var resolution = Resolve(vault, target: $"repo/src/{new string('a', 10)}/orders.cs");

        Assert.Empty(resolution.Loaded);
    }

    private static string Mode(string mode) =>
        "{\"pluginConfigs\": {\"agents-md@builtin\": {\"options\": {\"instructionFiles\": \"" + mode + "\"}}}}";

    [Fact]
    public void Without_claude_files_agents_md_loads_at_launch_and_on_read()
    {
        using var vault = new TempVault()
            .Write("home/.claude/CLAUDE.md", "user memory doesn't block AGENTS.md\n")
            .Write("repo/AGENTS.md", "root\n")
            .Write("repo/.claude/AGENTS.md", "root hidden\n")
            .Write("repo/src/api/AGENTS.md", "api\n");

        var resolution = Resolve(vault);

        Assert.Equal(
            [
                ("home/.claude/CLAUDE.md", InstructionScope.User, "claude-code/user-memory"),
                ("repo/AGENTS.md", InstructionScope.Project, "claude-code/agents-md"),
                ("repo/.claude/AGENTS.md", InstructionScope.Project, "claude-code/agents-md"),
            ],
            Loaded(vault, resolution, LoadTiming.AtLaunch));
        Assert.Equal([("repo/src/api/AGENTS.md", InstructionScope.Project, "claude-code/nested-agents-md")], Loaded(vault, resolution, LoadTiming.OnRead));
    }

    // Recorded in agents-md-mixed: one CLAUDE file above turns AGENTS.md off everywhere, nested ones too.
    [Fact]
    public void A_claude_file_above_hides_every_agents_md_by_default()
    {
        using var vault = new TempVault().Write("repo/CLAUDE.local.md", "local\n").Write("repo/AGENTS.md", "root\n").Write("repo/src/api/AGENTS.md", "api\n");

        var resolution = Resolve(vault);

        Assert.Equal(["repo/CLAUDE.local.md"], resolution.Loaded.Select(item => TestMachine.Relative(vault.Root, item.Path)));
        Assert.Equal(
            [("repo/AGENTS.md", "claude-code/agents-md-hidden", null), ("repo/src/api/AGENTS.md", "claude-code/agents-md-hidden", null)],
            Dropped(vault, resolution));
    }

    // Recorded in agents-md-and-claude-md and agents-md-two-levels.
    [Fact]
    public void With_both_files_agents_md_follows_each_directory_and_comes_first_on_read()
    {
        using var vault = new TempVault()
            .Write("home/.claude/settings.json", Mode("claude-md-and-agents-md"))
            .Write("repo/CLAUDE.md", "root\n")
            .Write("repo/AGENTS.md", "root agents\n")
            .Write("repo/CLAUDE.local.md", "local\n")
            .Write("repo/src/CLAUDE.md", "src\n")
            .Write("repo/src/AGENTS.md", "src agents\n")
            .Write("repo/src/api/CLAUDE.md", "api\n")
            .Write("repo/src/api/AGENTS.md", "api agents\n");

        var resolution = Resolve(vault, launch: "repo/src");

        Assert.Equal(
            ["repo/CLAUDE.md", "repo/CLAUDE.local.md", "repo/AGENTS.md", "repo/src/CLAUDE.md", "repo/src/AGENTS.md"],
            resolution.Loaded.Where(item => item.Timing == LoadTiming.AtLaunch).Select(item => TestMachine.Relative(vault.Root, item.Path)));
        Assert.Equal(
            ["repo/src/api/AGENTS.md", "repo/src/api/CLAUDE.md"],
            resolution.Loaded.Where(item => item.Timing == LoadTiming.OnRead).Select(item => TestMachine.Relative(vault.Root, item.Path)));
    }

    [Fact]
    public void Claude_md_mode_never_reads_agents_md()
    {
        using var vault = new TempVault().Write("home/.claude/settings.json", Mode("claude-md")).Write("repo/AGENTS.md", "root\n");

        var resolution = Resolve(vault);

        Assert.Empty(resolution.Loaded);
        Assert.Equal([("repo/AGENTS.md", "claude-code/agents-md-off", null)], Dropped(vault, resolution));
    }

    // The mode is read from user and managed settings only, never from the project.
    [Fact]
    public void The_mode_in_project_settings_is_ignored()
    {
        using var vault = new TempVault().Write("repo/.claude/settings.json", Mode("claude-md")).Write("repo/AGENTS.md", "root\n");

        Assert.Equal("claude-code/agents-md", Assert.Single(Resolve(vault).Loaded).Rule.Id);
    }

    [Fact]
    public void Managed_only_leaves_out_everything_but_the_managed_file_until_a_file_is_read()
    {
        using var vault = new TempVault()
            .Write("managed/managed-settings.json", Mode("managed-only"))
            .Write("managed/CLAUDE.md", "managed\n")
            .Write("home/.claude/CLAUDE.md", "user\n")
            .Write("repo/CLAUDE.md", "root\n")
            .Write("repo/.claude/rules/scoped.md", PathRule("src/api/**"))
            .Write("repo/src/api/CLAUDE.md", "api\n");

        var resolution = Resolve(vault);

        Assert.Equal(["managed/CLAUDE.md"], resolution.Loaded.Where(item => item.Timing == LoadTiming.AtLaunch).Select(item => TestMachine.Relative(vault.Root, item.Path)));
        Assert.Equal(
            ["repo/src/api/CLAUDE.md", "repo/.claude/rules/scoped.md"],
            resolution.Loaded.Where(item => item.Timing == LoadTiming.OnRead).Select(item => TestMachine.Relative(vault.Root, item.Path)));
        Assert.Equal(
            [("home/.claude/CLAUDE.md", "claude-code/managed-only", null), ("repo/CLAUDE.md", "claude-code/managed-only", null)],
            Dropped(vault, resolution));
    }

    // Recorded in agents-md-mixed: excludes reach imports, and merge across settings layers.
    [Fact]
    public void Claude_md_excludes_match_absolute_paths_from_every_layer_except_for_the_managed_file()
    {
        using var vault = new TempVault()
            .Write("home/.claude/settings.json", """{"claudeMdExcludes": ["**/docs/excluded.md", "**/managed/CLAUDE.md"]}""")
            .Write("repo/.claude/settings.local.json", """{"claudeMdExcludes": ["**/repo/CLAUDE.local.md"]}""")
            .Write("managed/CLAUDE.md", "managed\n")
            .Write("repo/CLAUDE.md", "@docs/excluded.md @docs/kept.md\n")
            .Write("repo/docs/excluded.md", "excluded\n")
            .Write("repo/docs/kept.md", "kept\n")
            .Write("repo/CLAUDE.local.md", "local\n");

        var resolution = Resolve(vault);

        Assert.Equal(["managed/CLAUDE.md", "repo/CLAUDE.md", "repo/docs/kept.md"], resolution.Loaded.Select(item => TestMachine.Relative(vault.Root, item.Path)));
        Assert.Equal(
            [("repo/docs/excluded.md", "claude-code/excluded", "repo/CLAUDE.md:1"), ("repo/CLAUDE.local.md", "claude-code/excluded", null)],
            Dropped(vault, resolution));
    }

    // Recorded in agents-md-mixed: a block comment goes with the blank lines after it; inline ones stay.
    [Fact]
    public void Block_html_comments_are_stripped_before_imports_and_bytes()
    {
        const string text = "# Project\n<!-- note -->\nKeep it small. <!-- inline -->\n\n<!--\n@docs/hidden.md\n-->\n\n```\n<!-- kept in code -->\n```\n";
        using var vault = new TempVault().Write("repo/CLAUDE.md", text).Write("repo/docs/hidden.md", "hidden\n");

        var loaded = Assert.Single(Resolve(vault).Loaded);

        Assert.Equal("# Project\nKeep it small. <!-- inline -->\n\n```\n<!-- kept in code -->\n```".Length, loaded.Bytes);
    }

    [Fact]
    public void A_file_over_4_mib_is_skipped()
    {
        using var vault = new TempVault().Write("repo/CLAUDE.md", new string('x', (4 * 1024 * 1024) + 1));

        var resolution = Resolve(vault);

        Assert.Empty(resolution.Loaded);
        Assert.Equal([("repo/CLAUDE.md", "claude-code/too-large", null)], Dropped(vault, resolution));
    }

    [Fact]
    public void Every_rule_cites_its_source()
    {
        Assert.All(ClaudeCodeRules.All, rule =>
        {
            Assert.StartsWith("claude-code/", rule.Id);
            Assert.False(string.IsNullOrWhiteSpace(rule.Label));
            Assert.False(string.IsNullOrWhiteSpace(rule.Summary));
            Assert.StartsWith("https://", rule.Source);
        });
    }
}

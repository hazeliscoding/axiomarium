using Axiomarium.Core.Health;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class InstructionFindingsTests
{
    private static IReadOnlyList<InstructionFinding> Check(TempVault vault) =>
        InstructionFindings.ForRepo(Path.Combine(vault.Root, "repo"), TestMachine.For(vault.Root));

    private static InstructionFinding Single(TempVault vault, string id) => Assert.Single(Check(vault), finding => finding.Id == id);

    [Fact]
    public void Dead_import_names_the_line_and_the_missing_file()
    {
        using var vault = new TempVault().Write("repo/CLAUDE.md", "project\nSee @docs/testing.md for tests.\n");

        var finding = Single(vault, "dead-import");

        Assert.Equal(
            new InstructionFinding(
                "dead-import", Severity.Warning, "CLAUDE.md", 2,
                "CLAUDE.md:2 imports docs/testing.md, which does not exist, so Claude Code loads nothing in its place.",
                "Restore the file, or remove the import."),
            finding);
    }

    [Fact]
    public void Dead_import_with_trailing_punctuation_says_to_remove_it()
    {
        using var vault = new TempVault()
            .Write("repo/CLAUDE.md", "See @docs/testing.md, then run the tests.\nRead @docs/missing.md, too.\n")
            .Write("repo/docs/testing.md", "testing\n");

        var findings = Check(vault).Where(finding => finding.Id == "dead-import").ToList();

        Assert.Equal(
            "CLAUDE.md:1 imports docs/testing.md with a \",\" after it, which Claude Code reads as part of the path, so it loads nothing.",
            findings[0].Message);
        Assert.Equal("Remove the \",\", or put a space before it.", findings[0].Fix);
        Assert.Equal(
            "CLAUDE.md:2 imports docs/missing.md with a \",\" after it, which Claude Code reads as part of the path, so it loads nothing.",
            findings[1].Message);
        Assert.Equal("Remove the \",\", or put a space before it, and restore docs/missing.md or remove the import.", findings[1].Fix);
    }

    [Fact]
    public void Import_too_deep_names_the_chain()
    {
        using var vault = new TempVault()
            .Write("repo/CLAUDE.md", "@docs/1.md\n")
            .Write("repo/docs/1.md", "@2.md\n")
            .Write("repo/docs/2.md", "@3.md\n")
            .Write("repo/docs/3.md", "@4.md\n")
            .Write("repo/docs/4.md", "four\n@5.md\n")
            .Write("repo/docs/5.md", "five\n");

        var finding = Single(vault, "import-too-deep");

        Assert.Equal(("docs/4.md", 2), (finding.File, finding.Line));
        Assert.Equal(
            "docs/4.md:2 imports docs/5.md, a fifth hop from CLAUDE.md. Claude Code follows four, so docs/5.md never loads.",
            finding.Message);
        Assert.Equal("Import docs/5.md from a file fewer hops from CLAUDE.md, or move its content up the chain.", finding.Fix);
    }

    [Fact]
    public void Agents_md_hidden_names_the_claude_file_and_the_import_to_add()
    {
        using var vault = new TempVault()
            .Write("repo/CLAUDE.md", "project\n")
            .Write("repo/AGENTS.md", "agents\n")
            .Write("repo/src/api/AGENTS.md", "api\n")
            .Write("repo/src/web/AGENTS.md", "web\n")
            .Write("repo/src/web/.claude/CLAUDE.md", "web claude\n");

        var findings = Check(vault).Where(finding => finding.Id == "agents-md-hidden").ToList();

        Assert.Equal(["AGENTS.md", "src/api/AGENTS.md", "src/web/AGENTS.md"], findings.Select(finding => finding.File));
        Assert.Equal(
            "Claude Code skips AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code.",
            findings[0].Message);
        Assert.Equal(
            ["Add @AGENTS.md to CLAUDE.md.", "Add a CLAUDE.md next to it that says @AGENTS.md.", "Add @../AGENTS.md to src/web/.claude/CLAUDE.md."],
            findings.Select(finding => finding.Fix));
        Assert.All(findings, finding => Assert.Equal(Severity.Warning, finding.Severity));
    }

    [Fact]
    public void Agents_md_above_the_repo_is_not_the_repos_problem()
    {
        using var vault = new TempVault()
            .Write("AGENTS.md", "for a folder of projects\n")
            .Write("repo/CLAUDE.md", "project\n")
            .Write("repo/AGENTS.md", "agents\n");

        Assert.Equal(["AGENTS.md"], Check(vault).Where(finding => finding.Id == "agents-md-hidden").Select(finding => finding.File));
    }

    [Fact]
    public void Agents_md_the_claude_file_imports_is_not_hidden()
    {
        using var vault = new TempVault().Write("repo/CLAUDE.md", "@AGENTS.md\n").Write("repo/AGENTS.md", "agents\n");

        Assert.DoesNotContain(Check(vault), finding => finding.Id == "agents-md-hidden");
    }

    [Fact]
    public void Rule_frontmatter_invalid_says_the_rule_loads_for_every_file()
    {
        using var vault = new TempVault().Write("repo/.claude/rules/api.md", "---\npaths:\n  - \"src/api/**\"\n bad: indent\n---\napi\n");

        var finding = Single(vault, "rule-frontmatter-invalid");

        Assert.Equal((".claude/rules/api.md", 1), (finding.File, finding.Line));
        Assert.Equal(".claude/rules/api.md has frontmatter that doesn't parse, so Claude Code ignores all of it and loads the rule for every file.", finding.Message);
        Assert.Equal("Fix the YAML between the --- lines, such as its indentation, so the rule's paths count again.", finding.Fix);
    }

    [Fact]
    public void Rule_matches_nothing_when_no_file_in_the_repo_matches()
    {
        using var vault = new TempVault()
            .Write("repo/.claude/rules/api.md", "---\npaths:\n  - \"src/api/**\"\n---\napi\n")
            .Write("repo/src/.claude/rules/web.md", "---\npaths:\n  - \"web/**\"\n---\nweb\n")
            .Write("repo/src/web/app.ts", "app\n")
            .Write("repo/src/app.cs", "app\n");

        var finding = Single(vault, "rule-matches-nothing");

        Assert.Equal(".claude/rules/api.md", finding.File);
        Assert.Equal(".claude/rules/api.md applies to src/api/**, which matches no file in the repo, so Claude Code never loads it.", finding.Message);
        Assert.Equal("Fix the patterns. They match paths relative to the repo root.", finding.Fix);
    }

    // Claude Code matches paths like .gitignore lines, so these match files deeper in the repo.
    [Fact]
    public void Rule_matches_nothing_stays_quiet_for_patterns_that_match_at_any_depth()
    {
        using var vault = new TempVault()
            .Write("repo/.claude/rules/name.md", "---\npaths: \"*.cs\"\n---\nname\n")
            .Write("repo/.claude/rules/folder.md", "---\npaths: \"api/**\"\n---\nfolder\n")
            .Write("repo/src/api/orders.cs", "orders\n");

        Assert.DoesNotContain(Check(vault), finding => finding.Id == "rule-matches-nothing");
    }

    [Fact]
    public void Rule_matches_nothing_names_an_invalid_pattern()
    {
        using var vault = new TempVault()
            .Write("repo/.claude/rules/api.md", "---\npaths:\n  - \"src/[ab.cs\"\n  - \"src/**\"\n---\napi\n")
            .Write("repo/src/app.cs", "app\n");

        var finding = Single(vault, "rule-matches-nothing");

        Assert.Equal(".claude/rules/api.md has the pattern \"src/[ab.cs\", which matches nothing: '[' at column 5 is never closed.", finding.Message);
        Assert.Equal("Fix the pattern, or remove it.", finding.Fix);
    }

    [Fact]
    public void Codex_byte_cap_names_the_cut_file_and_the_config()
    {
        using var vault = new TempVault()
            .Write("home/.codex/config.toml", "project_doc_max_bytes = 16\n")
            .Write("repo/AGENTS.md", new string('a', 40) + "\n");

        var finding = Single(vault, "codex-byte-cap");

        Assert.Equal(
            new InstructionFinding(
                "codex-byte-cap", Severity.Warning, "AGENTS.md", null,
                "AGENTS.md is cut to 16 of its 41 bytes, because Codex's project files share project_doc_max_bytes.",
                "Shorten the project files, or raise project_doc_max_bytes in ~/.codex/config.toml."),
            finding);
    }

    [Fact]
    public void Codex_byte_cap_lists_the_files_dropped_after_the_cut()
    {
        using var vault = new TempVault()
            .Folder("repo/.git")
            .Write("home/.codex/config.toml", "project_doc_max_bytes = 16\n")
            .Write("repo/AGENTS.md", new string('a', 40) + "\n")
            .Write("repo/src/AGENTS.md", "src\n");
        var machine = TestMachine.For(vault.Root);
        var repo = Path.Combine(vault.Root, "repo");

        var explanation = Explainer.Explain("src/app.cs", launchDirectory: "src", [Harness.Codex], machine, currentDirectory: repo);
        var finding = Assert.Single(InstructionFindings.For(explanation, machine));

        Assert.Equal(
            "AGENTS.md is cut to 16 of its 41 bytes, because Codex's project files share project_doc_max_bytes, and 1 later file is dropped: src/AGENTS.md.",
            finding.Message);
    }

    [Fact]
    public void Codex_empty_override_names_the_file_it_hides()
    {
        using var vault = new TempVault().Write("repo/AGENTS.override.md", "\n").Write("repo/AGENTS.md", "agents\n");

        var finding = Single(vault, "codex-empty-override");

        Assert.Equal(
            new InstructionFinding(
                "codex-empty-override", Severity.Warning, "AGENTS.override.md", null,
                "AGENTS.override.md is empty, and Codex picks it over AGENTS.md, so Codex loads nothing from that folder.",
                "Delete the empty AGENTS.override.md, or write the override in it."),
            finding);
    }

    [Fact]
    public void Duplicate_block_is_info_and_ignores_short_paragraphs()
    {
        using var vault = new TempVault()
            .Write("repo/CLAUDE.md", "# Project\n\nAlways run the formatter before you commit any change.\n")
            .Write("repo/.claude/rules/style.md", "# Project\n\nAlways run the formatter   before you commit\nany change.\n");

        var finding = Single(vault, "duplicate-block");

        Assert.Equal(
            new InstructionFinding(
                "duplicate-block", Severity.Info, ".claude/rules/style.md", 3,
                ".claude/rules/style.md:3 repeats a paragraph from CLAUDE.md:3. Both files load together, so it takes up context twice.",
                "Keep the paragraph in one of the files."),
            finding);
    }

    [Fact]
    public void Duplicate_block_counts_every_repeated_paragraph_once_per_pair()
    {
        const string Rules = "Always run the formatter before you commit any change.\n\nNever push to main without a review from the owner.\n";
        using var vault = new TempVault().Write("repo/CLAUDE.md", Rules).Write("repo/.claude/rules/style.md", Rules);

        var finding = Single(vault, "duplicate-block");

        Assert.Equal(
            ".claude/rules/style.md:1 repeats a paragraph from CLAUDE.md:1, and 1 more of its paragraphs repeats CLAUDE.md too. Both files load together, so they take up context twice.",
            finding.Message);
    }

    [Fact]
    public void Dead_link_skips_urls_anchors_and_code()
    {
        using var vault = new TempVault()
            .Write("repo/AGENTS.md", "See [the guide](docs/guide.md), [site](https://example.com), [top](#top) and [ok](docs/ok.md#part).\n\n```\n[x](missing.md)\n```\n\n`[y](missing.md)`\n\n[ref]: <docs/also missing.md>\n")
            .Write("repo/docs/ok.md", "ok\n");

        var findings = Check(vault).Where(finding => finding.Id == "dead-link").ToList();

        Assert.Equal([("AGENTS.md", 1), ("AGENTS.md", 9)], findings.Select(finding => (finding.File, finding.Line ?? 0)));
        Assert.Equal("AGENTS.md:1 links to docs/guide.md, which does not exist, so an agent that follows the link finds nothing.", findings[0].Message);
        Assert.Equal("Fix the link, or restore the file.", findings[0].Fix);
        Assert.Contains("links to docs/also missing.md,", findings[1].Message);
    }

    [Fact]
    public void Findings_come_once_in_severity_then_file_order()
    {
        using var vault = new TempVault()
            .Write("repo/CLAUDE.md", "project\nSee @docs/testing.md for tests.\n\nAlways run the formatter before you commit any change.\n")
            .Write("repo/.claude/rules/style.md", "Always run the formatter before you commit any change.\n")
            .Write("repo/AGENTS.md", "agents\n")
            .Write("repo/src/api/AGENTS.md", "api\n")
            .Write("repo/src/api/orders.cs", "class Orders { }\n");

        var findings = Check(vault);

        Assert.Equal(
            [("agents-md-hidden", "AGENTS.md"), ("dead-import", "CLAUDE.md"), ("agents-md-hidden", "src/api/AGENTS.md"), ("duplicate-block", ".claude/rules/style.md")],
            findings.Select(finding => (finding.Id, finding.File)));
    }

    [Fact]
    public void A_clean_repo_has_no_findings()
    {
        using var vault = new TempVault()
            .Write("home/.claude/CLAUDE.md", "user\n")
            .Write("repo/CLAUDE.md", "@AGENTS.md\n")
            .Write("repo/AGENTS.md", "See [the guide](docs/guide.md).\n")
            .Write("repo/docs/guide.md", "guide\n")
            .Write("repo/.claude/rules/api.md", "---\npaths:\n  - \"src/api/**\"\n---\napi\n")
            .Write("repo/src/api/orders.cs", "class Orders { }\n");

        Assert.Empty(Check(vault));
    }

    [Fact]
    public void Explain_finds_only_what_its_harnesses_load()
    {
        using var vault = new TempVault()
            .Folder("repo/.git")
            .Write("repo/CLAUDE.md", "project\n")
            .Write("repo/AGENTS.md", "agents\n");
        var machine = TestMachine.For(vault.Root);

        var explanation = Explainer.Explain("app.cs", launchDirectory: null, [Harness.Codex], machine, currentDirectory: Path.Combine(vault.Root, "repo"));

        Assert.Empty(InstructionFindings.For(explanation, machine));
    }

    [Fact]
    public void Every_finding_id_is_listed()
    {
        Assert.Equal(
            ["dead-import", "import-too-deep", "agents-md-hidden", "rule-frontmatter-invalid", "rule-matches-nothing", "codex-byte-cap", "codex-empty-override", "duplicate-block", "dead-link"],
            InstructionFindings.Ids);
    }
}

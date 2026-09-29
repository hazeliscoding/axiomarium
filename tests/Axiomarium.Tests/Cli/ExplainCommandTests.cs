using System.Text.Json.Nodes;
using Axiomarium.Cli;
using Axiomarium.Cli.Output;

namespace Axiomarium.Tests.Cli;

public class ExplainCommandTests
{
    // The README's demo, in miniature: a CLAUDE.md hides AGENTS.md from Claude Code, imports a missing
    // file, and a path rule matches the target; Codex reads the AGENTS.md files instead.
    private static TempVault Shop() => new TempVault()
        .Folder("repo/.git")
        .Write("home/.claude/CLAUDE.md", "user\n")
        .Write("home/.codex/AGENTS.md", "global\n")
        .Write("repo/CLAUDE.md", "project\nSee @docs/testing.md for tests.\n")
        .Write("repo/.claude/rules/backend.md", "---\npaths:\n  - \"src/api/**\"\n---\nbackend\n")
        .Write("repo/AGENTS.md", "agents\n")
        .Write("repo/src/api/AGENTS.md", "api agents\n")
        .Write("repo/src/api/orders.cs", "class Orders { }\n");

    private static (int ExitCode, string Output, string Error) Explain(TempVault vault, params string[] args) =>
        CliRun.Run(["explain", .. args], machine: TestMachine.For(vault.Root), currentDirectory: Path.Combine(vault.Root, "repo"));

    [Fact]
    public void Shows_what_each_harness_loads_and_drops()
    {
        using var vault = Shop();

        var (exitCode, output, error) = Explain(vault, "src/api/orders.cs");

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Empty(error);
        Assert.Equal(
            """
            AXM EXPLAIN // src/api/orders.cs

              CLAUDE CODE // launched at the repo root
              01  ~/.claude/CLAUDE.md        user                at launch
              02  CLAUDE.md                  project             at launch
              03  .claude/rules/backend.md   paths: src/api/**   when the file is read
              --  docs/testing.md            DROPPED     file is missing, imported by CLAUDE.md:2
              --  AGENTS.md                  DROPPED     a CLAUDE file exists and doesn't import it
              --  src/api/AGENTS.md          DROPPED     a CLAUDE file exists and doesn't import it

              CLAUDE CODE SKILLS // 13 listed · 6,230 of 8,000 characters, assuming a 200k-token context window
              01  13 built-in skills      built in   at launch

              CLAUDE CODE HOOKS // none at session start or around an edit

              CODEX // launched at the repo root
              01  ~/.codex/AGENTS.md         global              at launch
              02  AGENTS.md                  project             at launch
              --  src/api/AGENTS.md          NOT LOADED  below the launch directory

              CODEX SKILLS // none listed

              CODEX HOOKS // none at session start or around an edit

            WARNING  agents-md-hidden
                     Claude Code skips AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code.
                     Fix: Add @AGENTS.md to CLAUDE.md.

            WARNING  dead-import
                     CLAUDE.md:2 imports docs/testing.md, which does not exist, so Claude Code loads nothing in its place.
                     Fix: Restore the file, or remove the import.

            WARNING  agents-md-hidden
                     Claude Code skips src/api/AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code.
                     Fix: Add a CLAUDE.md next to it that says @AGENTS.md.

            2 harnesses · 5 loaded · 4 not loaded · 13 skills listed · 0 hooks run · 3 warnings

            """,
            output);
    }

    [Fact]
    public void Harness_and_cwd_narrow_the_explanation()
    {
        using var vault = Shop();

        var (_, output, _) = Explain(vault, "src/api/orders.cs", "--harness", "codex", "--cwd", "src/api");

        Assert.Equal(
            """
            AXM EXPLAIN // src/api/orders.cs

              CODEX // launched at src/api
              01  ~/.codex/AGENTS.md   global    at launch
              02  AGENTS.md            project   at launch
              03  src/api/AGENTS.md    project   at launch

              CODEX SKILLS // none listed

              CODEX HOOKS // none at session start or around an edit

            1 harness · 3 loaded · 0 not loaded · 0 skills listed · 0 hooks run

            """,
            output);
    }

    [Fact]
    public void Diff_shows_the_files_only_one_harness_loads()
    {
        using var vault = Shop();

        var (_, output, _) = Explain(vault, "src/api/orders.cs", "--diff");

        Assert.Equal(
            """
            AXM EXPLAIN // src/api/orders.cs // diff

              ONLY CLAUDE CODE
              01  ~/.claude/CLAUDE.md        user                at launch
              02  CLAUDE.md                  project             at launch
              03  .claude/rules/backend.md   paths: src/api/**   when the file is read

              ONLY CODEX
              01  ~/.codex/AGENTS.md         global              at launch
              02  AGENTS.md                  project             at launch

            3 files only in Claude Code · 2 only in Codex · 0 skills only in Claude Code · 0 only in Codex

            """,
            output);
    }

    // The skills-and-hooks scenario in miniature: a skill each harness lists, a path skill, a hidden one, and
    // hooks that do and don't run for the file.
    private static TempVault Tools() => new TempVault()
        .Folder("repo/.git")
        .Folder("home/.codex")
        .Write("repo/src/app.ts", "export const a = 1;\n")
        .Write("repo/.claude/skills/deploy/SKILL.md", "---\ndescription: Deploys the shop.\n---\nSteps.\n")
        .Write("repo/.claude/skills/typescript/SKILL.md", "---\ndescription: TypeScript.\npaths: \"src/**/*.ts\"\n---\nSteps.\n")
        .Write("repo/.claude/skills/docs/SKILL.md", "---\ndescription: The docs.\npaths: \"docs/**\"\n---\nSteps.\n")
        .Write("repo/.agents/skills/ship/SKILL.md", "---\ndescription: Ships the shop.\n---\nSteps.\n")
        .Write("repo/.claude/settings.json", """
            { "hooks": {
              "SessionStart": [ { "hooks": [ { "type": "command", "command": "axm hook session-doctor" } ] } ],
              "PostToolUse": [
                { "matcher": "Edit|Write", "hooks": [ { "type": "command", "command": "axm hook scope-sheriff", "if": "Edit(src/**)" } ] },
                { "matcher": "Edit|Write", "hooks": [ { "type": "command", "command": "lint-docs", "if": "Edit(docs/**)" } ] } ] } }
            """)
        .Write("home/.codex/hooks.json", """{ "hooks": { "PostToolUse": [ { "matcher": "apply_patch", "hooks": [ { "type": "command", "command": "axm hook scope-sheriff" } ] } ] } }""");

    [Fact]
    public void Shows_the_skills_each_harness_lists_and_the_hooks_that_run_for_the_file()
    {
        using var vault = Tools();

        var (_, output, _) = Explain(vault, "src/app.ts");

        Assert.Contains(
            """
              CLAUDE CODE SKILLS // 15 listed · 6,258 of 8,000 characters, assuming a 200k-token context window
              01  deploy               .claude/skills/deploy/SKILL.md       project              at launch
              02  13 built-in skills                                        built in             at launch
              03  typescript           .claude/skills/typescript/SKILL.md   paths: src/**/*.ts   when the file is read or edited
              --  docs                 .claude/skills/docs/SKILL.md         NOT LISTED  paths don't match

              CLAUDE CODE HOOKS // at session start and around an edit of the file, in a trusted workspace
              01  session start   .claude/settings.json   axm hook session-doctor   RUNS     project
              02  after edit      .claude/settings.json   axm hook scope-sheriff    RUNS     project, if Edit(src/**)
              --  after edit      .claude/settings.json   lint-docs                 NOT RUN  if Edit(docs/**) doesn't match

            """,
            output);
        // A Codex entry's size depends on the absolute path of its skill, so only the rows are pinned.
        Assert.Contains("  CODEX SKILLS // 1 listed · ", output);
        Assert.Contains(
            """
              01  ship   .agents/skills/ship/SKILL.md   repo   at launch

              CODEX HOOKS // at session start and around an edit of the file
              --  after edit   ~/.codex/hooks.json   axm hook scope-sheriff   NOT RUN  not trusted

            """,
            output);
        Assert.EndsWith("2 harnesses · 0 loaded · 0 not loaded · 16 skills listed · 2 hooks run\n", output);
    }

    [Fact]
    public void Diff_shows_the_skills_only_one_harness_lists_leaving_out_built_in_ones()
    {
        using var vault = Tools();

        var (_, output, _) = Explain(vault, "src/app.ts", "--diff");

        Assert.Contains(
            """
              ONLY CLAUDE CODE SKILLS
              01  deploy       .claude/skills/deploy/SKILL.md       project              at launch
              02  typescript   .claude/skills/typescript/SKILL.md   paths: src/**/*.ts   when the file is read or edited

            """,
            output);
        Assert.Contains("  ONLY CODEX SKILLS\n  01  ship   .agents/skills/ship/SKILL.md   repo   at launch\n", output);
        Assert.Contains("0 files only in Claude Code · 0 only in Codex · 2 skills only in Claude Code · 1 only in Codex", output);
    }

    [Fact]
    public void Json_lists_skills_the_listing_and_hooks_for_each_harness()
    {
        using var vault = Tools();

        var (_, output, _) = Explain(vault, "src/app.ts", "--json");

        var json = JsonNode.Parse(output)!;
        var claude = json["harnesses"]![0]!;
        Assert.Equal(
            """{"name":"deploy","path":".claude/skills/deploy/SKILL.md","timing":"at-launch","rule":"claude-code/project-skill","chars":27}""",
            claude["skills"]![0]!.ToJsonString());
        Assert.Equal(
            """{"name":"dataviz","path":null,"timing":"at-launch","rule":"claude-code/built-in-skill","chars":1447}""",
            claude["skills"]![1]!.ToJsonString());
        Assert.Equal(
            """{"name":"typescript","path":".claude/skills/typescript/SKILL.md","timing":"when-read","rule":"claude-code/paths-skill","chars":25,"patterns":["src/**/*.ts"]}""",
            claude["skills"]![14]!.ToJsonString());
        Assert.Equal(
            """[{"name":"docs","path":".claude/skills/docs/SKILL.md","rule":"claude-code/paths-skill-no-match"}]""",
            claude["notListed"]!.ToJsonString());
        Assert.Equal(
            """{"size":6258,"budget":8000,"unit":"characters","overBudget":false,"assumption":"a 200k-token context window","rule":"claude-code/skill-listing-budget"}""",
            claude["listing"]!.ToJsonString());
        Assert.Equal(
            """{"moment":"after-edit","path":".claude/settings.json","event":"PostToolUse","matcher":"Edit|Write","handler":"lint-docs","if":"Edit(docs/**)","input":"Edit","runs":false,"rule":"claude-code/hook-if-no-match"}""",
            claude["hooks"]![2]!.ToJsonString());

        var codexHook = json["harnesses"]![1]!["hooks"]![0]!;
        Assert.Equal(("untrusted", false, "codex/hook-untrusted"), (codexHook["trust"]!.GetValue<string>(), codexHook["runs"]!.GetValue<bool>(), codexHook["rule"]!.GetValue<string>()));
        Assert.StartsWith("sha256:", codexHook["hash"]!.GetValue<string>());
        Assert.NotNull(json["rules"]!["claude-code/paths-skill"]);
        Assert.Equal("if doesn't match", json["rules"]!["claude-code/hook-if-no-match"]!["label"]!.GetValue<string>());
    }

    private static TempVault Bare() => new TempVault()
        .Folder("repo/.git")
        .Folder("home/.codex")
        .Write("repo/src/app.ts", "export const a = 1;\n")
        .Write("repo/.claude/skills/bare/SKILL.md", "---\nname: bare\n---\nFirst line.\n")
        .Write("repo/.agents/skills/bare/SKILL.md", "---\nname: bare\n---\nFirst line.\n");

    [Fact]
    public void Says_why_a_skill_has_no_description()
    {
        using var vault = Bare();

        var (_, output, _) = Explain(vault, "src/app.ts");

        Assert.Contains("   at launch, first line as description\n", output);
        Assert.Contains("NOT LISTED  SKILL.md can't be read: it has no description\n", output);
    }

    [Fact]
    public void Json_says_why_a_skill_has_no_description()
    {
        using var vault = Bare();

        var (_, output, _) = Explain(vault, "src/app.ts", "--json");

        var json = JsonNode.Parse(output)!;
        var claude = json["harnesses"]![0]!["skills"]!.AsArray().Single(skill => skill!["name"]!.GetValue<string>() == "bare")!;
        var codex = json["harnesses"]![1]!["notListed"]![0]!;
        Assert.Equal("it has no description", claude["fallback"]!.GetValue<string>());
        Assert.Equal(("codex/skill-invalid", "it has no description"), (codex["rule"]!.GetValue<string>(), codex["detail"]!.GetValue<string>()));
    }

    [Fact]
    public void Diff_needs_both_harnesses()
    {
        using var vault = Shop();

        var (exitCode, _, error) = Explain(vault, "src/api/orders.cs", "--diff", "--harness", "codex");

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.StartsWith("axm: --diff compares two harnesses", error);
    }

    [Fact]
    public void Json_is_the_contract()
    {
        using var vault = Shop();

        var (exitCode, output, _) = Explain(vault, "src/api/orders.cs", "--json");

        Assert.Equal(AxmCli.Passed, exitCode);
        var json = JsonNode.Parse(output)!;
        Assert.Equal(1, json["schemaVersion"]!.GetValue<int>());
        Assert.Equal("src/api/orders.cs", json["target"]!.GetValue<string>());
        Assert.Equal(".", json["launchDirectory"]!.GetValue<string>());

        var claude = json["harnesses"]![0]!;
        Assert.Equal(("claude-code", "2.1.284"), (claude["harness"]!.GetValue<string>(), claude["confirmedWith"]!.GetValue<string>()));
        Assert.Equal(
            """{"path":".claude/rules/backend.md","scope":"project","timing":"when-read","rule":"claude-code/path-rule","bytes":8,"patterns":["src/api/**"]}""",
            claude["loaded"]![2]!.ToJsonString());
        Assert.Equal(
            """{"path":"docs/testing.md","rule":"claude-code/missing-import","importedFrom":"CLAUDE.md:2"}""",
            claude["dropped"]![0]!.ToJsonString());
        Assert.Equal(
            """{"path":"src/api/AGENTS.md","rule":"codex/below-launch"}""",
            json["harnesses"]![1]!["dropped"]![0]!.ToJsonString());

        Assert.Equal(
            """{"id":"dead-import","severity":"warning","file":"CLAUDE.md","line":2,"message":"CLAUDE.md:2 imports docs/testing.md, which does not exist, so Claude Code loads nothing in its place.","fix":"Restore the file, or remove the import."}""",
            json["findings"]![1]!.ToJsonString());
        Assert.Null(json["findings"]![0]!["line"]);

        var rule = json["rules"]!["codex/below-launch"]!;
        Assert.Equal("below the launch directory", rule["label"]!.GetValue<string>());
        Assert.True(rule["leftToModel"]!.GetValue<bool>());
        Assert.StartsWith("https://", rule["source"]!.GetValue<string>());
    }

    [Fact]
    public void A_target_whose_folder_does_not_exist_could_not_run()
    {
        using var vault = Shop();

        var (exitCode, output, error) = Explain(vault, "nowhere/at/all.cs");

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.Empty(output);
        Assert.StartsWith("axm: The folder ", error);
    }

    [Fact]
    public void A_folder_is_not_a_target()
    {
        using var vault = Shop();

        var (exitCode, output, error) = Explain(vault, "src/api");

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.Empty(output);
        Assert.StartsWith("axm: src/api is a folder.", error);
    }

    [Fact]
    public void Diff_is_text_only()
    {
        using var vault = Shop();

        var (exitCode, output, error) = Explain(vault, "src/api/orders.cs", "--diff", "--json");

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.Empty(output);
        Assert.StartsWith("axm: --diff is for reading", error);
    }

    [Fact]
    public void Imports_and_cut_files_say_so_on_their_row()
    {
        using var vault = new TempVault()
            .Folder("repo/.git")
            .Write("repo/CLAUDE.md", "project\nSee @docs/style.md\n")
            .Write("repo/docs/style.md", "style\n")
            .Write("repo/AGENTS.md", new string('a', 40) + "\n")
            .Write("home/.codex/config.toml", "project_doc_max_bytes = 16\n")
            .Write("repo/app.cs", "class App { }\n");

        var (_, output, _) = Explain(vault, "app.cs");

        Assert.Contains("  02  docs/style.md   import    at launch, imported by CLAUDE.md:2\n", output);
        Assert.Contains("  01  AGENTS.md       project   at launch, cut to 16 bytes\n", output);
    }

    [Fact]
    public void Outside_a_repo_paths_are_absolute_or_under_home()
    {
        using var vault = new TempVault().Write("home/.claude/CLAUDE.md", "user\n").Write("notes/todo.md", "todo\n");
        var notes = Path.Combine(vault.Root, "notes");

        var (_, output, _) = CliRun.Run(["explain", "todo.md", "--harness", "claude-code"], machine: TestMachine.For(vault.Root), currentDirectory: notes);

        var shown = notes.Replace('\\', '/');
        Assert.StartsWith($"AXM EXPLAIN // {shown}/todo.md\n\n  CLAUDE CODE // launched at {shown}\n  01  ~/.claude/CLAUDE.md", output);
    }

    [Fact]
    public void Terminal_output_is_plain_output_plus_color_and_kaomoji()
    {
        using var vault = Shop();

        var (_, plain, _) = Explain(vault, "src/api/orders.cs");
        var (_, fancy, _) = CliRun.Run(["explain", "src/api/orders.cs"], terminal: true, machine: TestMachine.For(vault.Root), currentDirectory: Path.Combine(vault.Root, "repo"));

        Assert.Contains("\u001b[", fancy);
        Assert.Equal(plain, CliRun.StripColor(fancy).Replace($"  {Kaomoji.WarningsOnly}", ""));
    }
}

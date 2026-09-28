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

              CODEX // launched at the repo root
              01  ~/.codex/AGENTS.md         global              at launch
              02  AGENTS.md                  project             at launch
              --  src/api/AGENTS.md          NOT LOADED  below the launch directory

            WARNING  agents-md-hidden
                     Claude Code skips AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code.
                     Fix: Add @AGENTS.md to CLAUDE.md.

            WARNING  dead-import
                     CLAUDE.md:2 imports docs/testing.md, which does not exist, so Claude Code loads nothing in its place.
                     Fix: Restore the file, or remove the import.

            WARNING  agents-md-hidden
                     Claude Code skips src/api/AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code.
                     Fix: Add a CLAUDE.md next to it that says @AGENTS.md.

            2 harnesses · 5 loaded · 4 not loaded · 3 warnings

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

            1 harness · 3 loaded · 0 not loaded

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

            3 only in Claude Code · 2 only in Codex

            """,
            output);
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

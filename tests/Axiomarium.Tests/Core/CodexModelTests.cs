using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class CodexModelTests
{
    // A repo with a .git marker at repo/, and a home at home/ whose .codex is CODEX_HOME.
    private static TempVault Repo() => new TempVault().Folder("repo/.git").Folder("home/.codex");

    private static Resolution Resolve(TempVault vault, string launch = "repo", string target = "repo/src/api/orders.cs") =>
        CodexModel.Resolve(Path.Combine(vault.Root, launch), Path.Combine(vault.Root, target), Machine(vault));

    private static Machine Machine(TempVault vault) => TestMachine.For(vault.Root);

    private static string Relative(TempVault vault, string path) => Path.GetRelativePath(vault.Root, path).Replace('\\', '/');

    private static (string, int, bool)[] Loaded(TempVault vault, Resolution resolution) =>
        [.. resolution.Loaded.Select(item => (Relative(vault, item.Path), item.Bytes, item.Cut))];

    private static (string, string)[] Dropped(TempVault vault, Resolution resolution) =>
        [.. resolution.Dropped.Select(item => (Relative(vault, item.Path), item.Rule.Id))];

    [Fact]
    public void The_global_file_loads_first_trimmed_then_the_chain_from_the_root_down()
    {
        using var vault = Repo()
            .Write("home/.codex/AGENTS.md", "  global  \n")
            .Write("repo/AGENTS.md", "root\n")
            .Write("repo/src/AGENTS.md", "src\n");

        var resolution = Resolve(vault, launch: "repo/src");

        Assert.Equal([("home/.codex/AGENTS.md", 6, false), ("repo/AGENTS.md", 5, false), ("repo/src/AGENTS.md", 4, false)], Loaded(vault, resolution));
        Assert.Equal(
            [("home/.codex/AGENTS.md", "codex/global"), ("repo/AGENTS.md", "codex/project-chain"), ("repo/src/AGENTS.md", "codex/project-chain")],
            resolution.Loaded.Select(item => (Relative(vault, item.Path), item.Rule.Id)));
        Assert.Equal([InstructionScope.User, InstructionScope.Project, InstructionScope.Project], resolution.Loaded.Select(item => item.Scope));
        Assert.All(resolution.Loaded, item => Assert.Equal(LoadTiming.AtLaunch, item.Timing));
    }

    [Fact]
    public void A_global_override_that_isnt_empty_replaces_the_global_agents_md()
    {
        using var vault = Repo().Write("home/.codex/AGENTS.override.md", "override\n").Write("home/.codex/AGENTS.md", "global\n");

        var resolution = Resolve(vault);

        Assert.Equal([("home/.codex/AGENTS.override.md", 8, false)], Loaded(vault, resolution));
        Assert.Equal([("home/.codex/AGENTS.md", "codex/global-override")], Dropped(vault, resolution));
    }

    [Fact]
    public void An_empty_global_override_falls_through_to_agents_md()
    {
        using var vault = Repo().Write("home/.codex/AGENTS.override.md", "\n").Write("home/.codex/AGENTS.md", "global\n");

        Assert.Equal([("home/.codex/AGENTS.md", 6, false)], Loaded(vault, Resolve(vault)));
    }

    [Fact]
    public void Files_below_the_launch_directory_are_not_loaded_by_the_harness()
    {
        using var vault = Repo().Write("repo/AGENTS.md", "root\n").Write("repo/src/api/AGENTS.md", "api\n");

        var resolution = Resolve(vault);

        Assert.Equal([("repo/AGENTS.md", 5, false)], Loaded(vault, resolution));
        Assert.Equal([("repo/src/api/AGENTS.md", "codex/below-launch")], Dropped(vault, resolution));
    }

    [Fact]
    public void A_directory_contributes_its_first_file_only()
    {
        using var vault = Repo().Write("repo/AGENTS.override.md", "override\n").Write("repo/AGENTS.md", "root\n");

        var resolution = Resolve(vault);

        Assert.Equal([("repo/AGENTS.override.md", 9, false)], Loaded(vault, resolution));
        Assert.Equal([("repo/AGENTS.md", "codex/one-per-directory")], Dropped(vault, resolution));
    }

    [Fact]
    public void An_empty_override_hides_the_agents_md_next_to_it()
    {
        using var vault = Repo().Write("repo/AGENTS.override.md", "").Write("repo/AGENTS.md", "root\n");

        var resolution = Resolve(vault);

        Assert.Empty(resolution.Loaded);
        Assert.Equal([("repo/AGENTS.md", "codex/empty-override")], Dropped(vault, resolution));
    }

    [Fact]
    public void Fallback_filenames_load_when_a_directory_has_no_agents_md()
    {
        using var vault = Repo()
            .Write("home/.codex/config.toml", "project_doc_fallback_filenames = [\"TEAM.md\", \"../escape.md\", \".\"]\n")
            .Write("repo/TEAM.md", "team\n");

        Assert.Equal([("repo/TEAM.md", 5, false)], Loaded(vault, Resolve(vault)));
    }

    [Fact]
    public void The_byte_budget_cuts_the_file_that_crosses_it_and_drops_the_rest_but_spares_the_global_file()
    {
        using var vault = Repo()
            .Write("home/.codex/config.toml", "project_doc_max_bytes = 8\n")
            .Write("home/.codex/AGENTS.md", "global file longer than the budget\n")
            .Write("repo/AGENTS.md", "root\n")
            .Write("repo/src/AGENTS.md", "source\n")
            .Write("repo/src/api/AGENTS.md", "api\n");

        var resolution = Resolve(vault, launch: "repo/src/api");

        Assert.Equal([("home/.codex/AGENTS.md", 34, false), ("repo/AGENTS.md", 5, false), ("repo/src/AGENTS.md", 3, true)], Loaded(vault, resolution));
        Assert.Equal([("repo/src/api/AGENTS.md", "codex/byte-budget")], Dropped(vault, resolution));
    }

    [Fact]
    public void Without_a_project_root_marker_only_the_launch_directory_counts()
    {
        using var vault = new TempVault().Folder("home/.codex").Write("repo/AGENTS.md", "root\n").Write("repo/src/AGENTS.md", "src\n");

        Assert.Equal([("repo/src/AGENTS.md", 4, false)], Loaded(vault, Resolve(vault, launch: "repo/src", target: "repo/src/app.cs")));
    }

    // Codex looks up trusted and untrusted [projects] entries alike, so an untrusted one also counts only with the
    // path Codex writes: no forward slashes on Windows, no trailing separator, and in any case on Windows.
    [Fact]
    public void An_untrusted_entry_counts_only_with_the_path_codex_writes()
    {
        using var vault = Repo().Write("repo/AGENTS.md", "root\n");
        var repo = Path.Combine(vault.Root, "repo");
        var otherSlashes = repo.Replace(Path.DirectorySeparatorChar, Path.DirectorySeparatorChar == '\\' ? '/' : '\\');
        bool Drops(string entry)
        {
            vault.Write("home/.codex/config.toml", $"[projects.'{entry}']\ntrust_level = \"untrusted\"\n");
            return Dropped(vault, Resolve(vault)).Contains(("repo/AGENTS.md", "codex/untrusted"));
        }

        Assert.Equal(
            [true, false, false, OperatingSystem.IsWindows()],
            [Drops(repo), Drops(otherSlashes), Drops(repo + Path.DirectorySeparatorChar), Drops(repo.ToUpperInvariant())]);
    }

    [Fact]
    public void An_untrusted_project_loads_only_the_global_file()
    {
        using var vault = Repo().Write("home/.codex/AGENTS.md", "global\n").Write("repo/AGENTS.md", "root\n");
        var repo = Path.Combine(vault.Root, "repo");
        vault.Write("home/.codex/config.toml", $"[projects.'{repo}']\ntrust_level = \"untrusted\"\n");

        var resolution = Resolve(vault);

        Assert.Equal([("home/.codex/AGENTS.md", 6, false)], Loaded(vault, resolution));
        Assert.Equal([("repo/AGENTS.md", "codex/untrusted")], Dropped(vault, resolution));
    }

    [Fact]
    public void Every_rule_cites_its_source()
    {
        Assert.All(CodexRules.All, rule =>
        {
            Assert.StartsWith("codex/", rule.Id);
            Assert.False(string.IsNullOrWhiteSpace(rule.Label));
            Assert.False(string.IsNullOrWhiteSpace(rule.Summary));
            Assert.StartsWith("https://", rule.Source);
        });
    }
}

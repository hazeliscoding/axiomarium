using System.Runtime.InteropServices;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class ExplainerTests
{
    private static readonly Harness[] Both = [Harness.ClaudeCode, Harness.Codex];

    [Fact]
    public void The_launch_directory_defaults_to_the_repo_root()
    {
        using var vault = new TempVault().Folder("repo/.git").Write("repo/src/app.cs", "class App { }\n");

        var explanation = Explainer.Explain("src/app.cs", launchDirectory: null, Both, TestMachine.For(vault.Root), currentDirectory: Path.Combine(vault.Root, "repo"));

        Assert.Equal(Path.Combine(vault.Root, "repo"), explanation.RepoRoot);
        Assert.Equal(Path.Combine(vault.Root, "repo"), explanation.Launch);
        Assert.Equal(Path.Combine(vault.Root, "repo", "src", "app.cs"), explanation.Target);
        Assert.Equal(Both, explanation.Harnesses.Select(harness => harness.Harness));
    }

    [Fact]
    public void Cwd_sets_the_launch_directory()
    {
        using var vault = new TempVault().Folder("repo/.git").Write("repo/src/app.cs", "class App { }\n");

        var explanation = Explainer.Explain(
            Path.Combine(vault.Root, "repo", "src", "app.cs"), launchDirectory: "src", [Harness.Codex], TestMachine.For(vault.Root), currentDirectory: Path.Combine(vault.Root, "repo"));

        Assert.Equal(Path.Combine(vault.Root, "repo", "src"), explanation.Launch);
        Assert.Equal("0.156.1", Assert.Single(explanation.Harnesses).ConfirmedWith);
    }

    [Fact]
    public void Without_a_repo_the_launch_directory_is_the_current_directory()
    {
        using var vault = new TempVault().Write("notes/todo.md", "todo\n");

        var explanation = Explainer.Explain("notes/todo.md", launchDirectory: null, Both, TestMachine.For(vault.Root), currentDirectory: vault.Root);

        Assert.Null(explanation.RepoRoot);
        Assert.Equal(vault.Root, explanation.Launch);
    }

    [Fact]
    public void The_diff_lists_the_files_only_one_harness_loads()
    {
        using var vault = new TempVault()
            .Folder("repo/.git")
            .Write("repo/CLAUDE.md", "claude\n")
            .Write("repo/AGENTS.md", "agents\n")
            .Write("repo/docs/both.md", "both\n")
            .Write("repo/app.cs", "class App { }\n");
        vault.Write("repo/CLAUDE.md", "claude @AGENTS.md\n");

        var explanation = Explainer.Explain("app.cs", launchDirectory: null, Both, TestMachine.For(vault.Root), currentDirectory: Path.Combine(vault.Root, "repo"));
        var (onlyClaudeCode, onlyCodex) = Explainer.Diff(explanation.Harnesses[0].Resolution, explanation.Harnesses[1].Resolution);

        Assert.Equal([Path.Combine(vault.Root, "repo", "CLAUDE.md")], onlyClaudeCode.Select(item => item.Path));
        Assert.Empty(onlyCodex);
    }

    [Fact]
    public void The_machine_comes_from_the_environment_with_the_harnesses_defaults()
    {
        var home = Path.Combine(Path.GetTempPath(), "home-of-dev");
        var launch = Path.Combine(Path.GetTempPath(), "shop");

        var defaults = Machine.FromEnvironment(new Dictionary<string, string?> { ["HOME"] = home, ["USERPROFILE"] = home }, launch);
        var overridden = Machine.FromEnvironment(
            new Dictionary<string, string?> { ["HOME"] = home, ["USERPROFILE"] = home, ["CODEX_HOME"] = "/codex", ["CLAUDE_CONFIG_DIR"] = "/claude" }, launch);

        Assert.Equal((home, Path.Combine(home, ".codex"), Path.Combine(home, ".claude")), (defaults.Home, defaults.CodexHome, defaults.ClaudeConfig));
        Assert.Equal(Path.GetPathRoot(launch), defaults.FileSystemRoot);
        Assert.Equal(("/codex", "/claude"), (overridden.CodexHome, overridden.ClaudeConfig));
    }

    // Codex canonicalizes CODEX_HOME when it's set, and keys each user hook by that spelling: hooks/list on Windows
    // answered a lowercase drive letter and an upper-cased folder with the folder as it is on disk (0.156.1, 2026-10-04).
    [Fact]
    public void A_codex_home_that_is_set_takes_its_spelling_on_disk_with_links_followed()
    {
        using var vault = new TempVault().Folder("home/.codex");
        var onDisk = Path.Combine(vault.Root, "home", ".codex");
        string respelled;
        if (OperatingSystem.IsWindows())
        {
            respelled = char.ToLowerInvariant(onDisk[0]) + onDisk[1..^@"home\.codex".Length] + @"HOME\.CODEX";
        }
        else
        {
            respelled = Path.Combine(vault.Root, "link");
            Directory.CreateSymbolicLink(respelled, onDisk);
        }

        string CodexHome(string value) =>
            Machine.FromEnvironment(new Dictionary<string, string?> { ["HOME"] = vault.Root, ["USERPROFILE"] = vault.Root, ["CODEX_HOME"] = value }, vault.Root).CodexHome;

        var canonical = CodexHome(respelled);
        Assert.Equal(CodexHome(onDisk), canonical);
        Assert.EndsWith(Path.Combine("home", ".codex"), canonical);
        Assert.False(char.IsLower(canonical[0]));
    }

    [Fact]
    public void Paths_show_relative_to_the_repo_root_then_home_then_whole()
    {
        var root = Path.Combine(Path.GetTempPath(), "machine");
        var repo = Path.Combine(root, "home", "shop");
        var home = Path.Combine(root, "home");

        Assert.Equal("src/api/notes.", DisplayPath.Of(Path.Combine(repo, "src", "api") + Path.DirectorySeparatorChar + "notes.", repo, home));
        Assert.Equal(".", DisplayPath.Of(repo, repo, home));
        Assert.Equal("~/.claude/CLAUDE.md", DisplayPath.Of(Path.Combine(home, ".claude", "CLAUDE.md"), repo, home));
        Assert.Equal("~", DisplayPath.Of(home, repo, home));
        Assert.Equal(Path.Combine(root, "etc", "AGENTS.md").Replace('\\', '/'), DisplayPath.Of(Path.Combine(root, "etc", "AGENTS.md"), repo, home));
        Assert.Equal("~/shop/CLAUDE.md", DisplayPath.Of(Path.Combine(repo, "CLAUDE.md"), repoRoot: null, home));
    }

    [Theory]
    [InlineData("WINDOWS", @"C:\Program Files\ClaudeCode")]
    [InlineData("OSX", "/Library/Application Support/ClaudeCode")]
    [InlineData("LINUX", "/etc/claude-code")]
    public void Claude_codes_managed_folder_depends_on_the_os(string os, string expected)
    {
        Assert.Equal(expected, Machine.ClaudeManagedFolder(OSPlatform.Create(os)));
    }
}

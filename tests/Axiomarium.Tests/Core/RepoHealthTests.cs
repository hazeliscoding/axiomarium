using Axiomarium.Core.Health;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class RepoHealthTests
{
    private static HealthReport Examine(TempVault vault, string folder = "repo") =>
        Doctor.Examine(Path.Combine(vault.Root, folder), TestMachine.For(vault.Root)).Report!;

    [Fact]
    public void A_repo_without_a_vault_gets_its_instructions_checked()
    {
        using var vault = new TempVault().Folder("repo/.git").Write("repo/CLAUDE.md", "project\n").Write("repo/AGENTS.md", "agents\n");

        var report = Examine(vault);

        Assert.Null(report.Vault);
        Assert.Equal(
            [("CLAUDE.md", [Harness.ClaudeCode]), ("AGENTS.md", [Harness.Codex])],
            report.Instructions.Files.Select(file => (file.Path, file.LoadedBy.ToArray())));
        Assert.Equal(["agents-md-hidden"], report.Instructions.Findings.Select(finding => finding.Id));
        Assert.Equal((0, 1, 0), (report.ErrorCount, report.WarningCount, report.InfoCount));
    }

    [Fact]
    public void A_file_no_harness_loads_is_listed_with_nobody()
    {
        using var vault = new TempVault()
            .Write("repo/.claude/rules/api.md", "---\npaths:\n  - \"src/api/**\"\n---\napi\n")
            .Write("repo/src/web/app.ts", "app\n");

        var report = Examine(vault);

        var rule = Assert.Single(report.Instructions.Files);
        Assert.Equal((".claude/rules/api.md", 0), (rule.Path, rule.LoadedBy.Count));
    }

    [Fact]
    public void A_subfolder_is_checked_from_the_repo_root_with_its_vault()
    {
        using var vault = new TempVault()
            .Folder("repo/.git")
            .Write("repo/agents/determinism-auditor/asset.yaml", SampleManifests.Valid)
            .Write("repo/agents/determinism-auditor/agent.md", "# determinism-auditor\n")
            .Write("repo/CLAUDE.md", "See @docs/missing.md\n")
            .Write("repo/src/app.cs", "class App { }\n");

        var report = Examine(vault, "repo/src");

        Assert.Equal(Path.Combine(vault.Root, "repo"), report.RepoRoot);
        Assert.Equal("determinism-auditor", Assert.Single(report.Vault!.Assets).Name);
        Assert.Equal(["dead-import"], report.Instructions.Findings.Select(finding => finding.Id));
    }

    [Fact]
    public void Ignored_paths_are_neither_listed_nor_checked_but_counted()
    {
        using var vault = new TempVault()
            .Write("repo/axiomarium.yaml", "doctor:\n  ignore:\n    - fixtures/\n")
            .Write("repo/CLAUDE.md", "@AGENTS.md\n")
            .Write("repo/AGENTS.md", "agents\n")
            .Write("repo/fixtures/broken/CLAUDE.md", "See @docs/missing.md\n")
            .Write("repo/fixtures/broken/.claude/rules/api.md", "---\npaths: [src/api/**\n---\napi\n");

        var report = Examine(vault);

        Assert.Empty(report.Config);
        Assert.Empty(report.Instructions.Findings);
        Assert.Equal(["CLAUDE.md", "AGENTS.md"], report.Instructions.Files.Select(file => file.Path));
        Assert.Equal(2, report.Instructions.Ignored);
    }

    [Fact]
    public void Without_the_ignore_list_the_broken_fixture_is_found()
    {
        using var vault = new TempVault()
            .Write("repo/CLAUDE.md", "@AGENTS.md\n")
            .Write("repo/AGENTS.md", "agents\n")
            .Write("repo/fixtures/broken/CLAUDE.md", "See @docs/missing.md\n");

        var report = Examine(vault);

        Assert.Equal(["dead-import"], report.Instructions.Findings.Select(finding => finding.Id));
        Assert.Equal(0, report.Instructions.Ignored);
    }

    [Fact]
    public void An_invalid_axiomarium_yaml_is_an_error_at_its_line()
    {
        using var vault = new TempVault().Write("repo/axiomarium.yaml", "doctor:\n  ignore:\n    - fixtures/\n  skip: []\n");

        var report = Examine(vault);

        var error = Assert.Single(report.Config);
        Assert.Equal((Severity.Error, "axiomarium.yaml", 4, "Unknown field: doctor.skip"), (error.Severity, error.File, error.Location?.Line, error.Message));
        Assert.Equal(1, report.ErrorCount);
    }

    [Fact]
    public void An_invalid_ignore_pattern_is_an_error_at_its_line()
    {
        using var vault = new TempVault().Write("repo/axiomarium.yaml", "doctor:\n  ignore:\n    - \"fixtures/{a\"\n");

        var error = Assert.Single(Examine(vault).Config);

        Assert.Equal((Severity.Error, 3), (error.Severity, error.Location?.Line));
        Assert.StartsWith("doctor.ignore[0] isn't a valid pattern: ", error.Message);
    }

    [Fact]
    public void A_missing_folder_could_not_run()
    {
        using var vault = new TempVault();

        var result = Doctor.Examine(Path.Combine(vault.Root, "nowhere"), TestMachine.For(vault.Root));

        Assert.Null(result.Report);
        Assert.Equal(VaultProblemKind.FolderMissing, result.Problem!.Kind);
    }

    [Fact]
    public void This_repo_is_healthy_once_its_deliberately_broken_fixtures_are_ignored()
    {
        var machine = TestMachine.For(RepoRoot.Path);

        var report = Doctor.Examine(RepoRoot.Path, machine).Report!;

        Assert.Equal((0, 0), (report.ErrorCount, report.WarningCount));
        Assert.True(report.Instructions.Ignored > 0);
        Assert.NotEmpty(InstructionFindings.Check(RepoRoot.Path, machine, ignore: []).Findings);
    }
}

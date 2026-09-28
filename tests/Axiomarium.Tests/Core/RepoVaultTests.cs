using Axiomarium.Core.Health;

namespace Axiomarium.Tests.Core;

// The repo's own vault has to pass its own doctor.
public class RepoVaultTests
{
    [Fact]
    public void The_repo_vault_is_healthy()
    {
        var result = Doctor.Run(RepoRoot.Path);

        Assert.Null(result.Problem);
        Assert.Empty(result.Report!.Diagnostics);
        Assert.Contains(result.Report.Assets, asset => asset.Name == "determinism-auditor");
    }

    // The authoring skill teaches by example, so its manifest has to stay valid as the schemas change.
    [Fact]
    public void The_authoring_skills_manifest_example_is_valid()
    {
        var skill = File.ReadAllText(Path.Combine(RepoRoot.Path, "skills", "agent-asset-authoring", "skill.md")).ReplaceLineEndings("\n");
        var start = skill.IndexOf("```yaml\n", StringComparison.Ordinal) + "```yaml\n".Length;
        var example = skill[start..skill.IndexOf("```", start, StringComparison.Ordinal)];
        using var vault = new TempVault().Asset("skills/release-notes", example);

        var result = Doctor.Run(vault.Root);

        Assert.Empty(result.Report!.Diagnostics);
    }
}

using Axiomarium.Core.Health;

namespace Axiomarium.Tests.Core;

// The repo's own vault has to pass its own doctor.
public class RepoVaultTests
{
    [Fact]
    public void The_repo_vault_is_healthy()
    {
        var result = Doctor.Run(RepoRoot.Path);

        Assert.Null(result.CouldNotRun);
        Assert.Empty(result.Report!.Diagnostics);
        Assert.Contains(result.Report.Assets, asset => asset.Name == "determinism-auditor");
    }
}

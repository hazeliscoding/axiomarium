using Axiomarium.Core.Instructions;

namespace Axiomarium.Core.Health;

/// <summary>What <see cref="Doctor.Examine(string, Instructions.Machine)"/> found in a repo: its vault, if it has one, its configuration and its instruction files.</summary>
/// <param name="RepoRoot">The repo's root: the nearest folder with a <c>.git</c>, or the folder examined outside a repo.</param>
/// <param name="Vault">The vault's report, or <see langword="null"/> when neither the folder examined nor the repo root is a vault.</param>
/// <param name="Config">The problems in <c>axiomarium.yaml</c>, which are all errors. Empty without the file.</param>
/// <param name="Instructions">The instruction files, launched from the repo root, and their findings.</param>
public sealed record HealthReport(string RepoRoot, DoctorReport? Vault, IReadOnlyList<Diagnostic> Config, InstructionCheck Instructions)
{
    /// <summary>How many errors: invalid assets and problems in <c>axiomarium.yaml</c>. Any error fails the doctor.</summary>
    public int ErrorCount => (Vault?.ErrorCount ?? 0) + Config.Count(diagnostic => diagnostic.Severity == Severity.Error);

    /// <summary>How many warnings, from the vault and the instruction findings. Warnings don't fail the doctor.</summary>
    public int WarningCount =>
        (Vault?.WarningCount ?? 0) + Instructions.Findings.Count(finding => finding.Severity == Severity.Warning);

    /// <summary>How many instruction findings are info, meaning waste.</summary>
    public int InfoCount => Instructions.Findings.Count(finding => finding.Severity == Severity.Info);
}

/// <summary>Either a report, or the reason the doctor couldn't run.</summary>
/// <param name="Report">The report, when the doctor ran.</param>
/// <param name="Problem">Why it couldn't run: the folder is missing or is a file.</param>
public sealed record HealthResult(HealthReport? Report, VaultProblem? Problem);

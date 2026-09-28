using Axiomarium.Core.Assets;

namespace Axiomarium.Core.Health;

/// <summary>What <see cref="Doctor.Run"/> found in a vault.</summary>
/// <param name="Assets">Every asset folder, by kind and then name.</param>
/// <param name="Diagnostics">Every problem, by file and then line.</param>
public sealed record DoctorReport(IReadOnlyList<DiscoveredAsset> Assets, IReadOnlyList<Diagnostic> Diagnostics)
{
    /// <summary>How many diagnostics are errors.</summary>
    public int ErrorCount => Diagnostics.Count(diagnostic => diagnostic.Severity == Severity.Error);

    /// <summary>How many diagnostics are warnings.</summary>
    public int WarningCount => Diagnostics.Count(diagnostic => diagnostic.Severity == Severity.Warning);

    /// <summary>The diagnostics for files inside <paramref name="asset"/>'s folder, or for the folder itself.</summary>
    /// <param name="asset">An asset from <see cref="Assets"/>.</param>
    /// <returns>Its diagnostics, in report order.</returns>
    public IEnumerable<Diagnostic> DiagnosticsFor(DiscoveredAsset asset) =>
        Diagnostics.Where(diagnostic => diagnostic.File == asset.Folder || diagnostic.File.StartsWith(asset.Folder + "/", StringComparison.Ordinal));
}

/// <summary>Why a folder couldn't be checked as a vault.</summary>
public enum VaultProblemKind
{
    /// <summary>Nothing exists at the path.</summary>
    FolderMissing,

    /// <summary>The path is a file.</summary>
    NotAFolder,

    /// <summary>The folder has none of the kind folders, such as <c>agents/</c>.</summary>
    NotAVault,
}

/// <summary>Why a folder couldn't be checked as a vault.</summary>
/// <param name="Kind">What went wrong, for callers that respond differently to each.</param>
/// <param name="Message">A sentence that says what went wrong and names the path.</param>
public sealed record VaultProblem(VaultProblemKind Kind, string Message);

/// <summary>Either a report, or the reason the doctor couldn't run.</summary>
/// <param name="Report">The report, when the doctor ran.</param>
/// <param name="Problem">Why it couldn't run, when it didn't.</param>
public sealed record DoctorResult(DoctorReport? Report, VaultProblem? Problem);

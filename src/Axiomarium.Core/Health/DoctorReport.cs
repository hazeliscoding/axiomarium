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

/// <summary>Either a report, or the reason the doctor couldn't run.</summary>
/// <param name="Report">The report, when the doctor ran.</param>
/// <param name="CouldNotRun">Why it couldn't run, such as a folder that isn't a vault.</param>
public sealed record DoctorResult(DoctorReport? Report, string? CouldNotRun);

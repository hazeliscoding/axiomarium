namespace Axiomarium.Core.Health;

/// <summary>What a doctor run has found so far, filled in asset by asset.</summary>
/// <remarks>
/// References and maturity claims are collected first and checked once every asset is known, because
/// they depend on the whole vault and on the usage log.
/// </remarks>
internal sealed class Findings
{
    /// <summary>Every problem found so far, in the order found.</summary>
    public List<Diagnostic> Diagnostics { get; } = [];

    /// <summary>The assets that valid manifests point to.</summary>
    public List<Reference> References { get; } = [];

    /// <summary>The maturity each valid manifest claims.</summary>
    public List<MaturityClaim> Claims { get; } = [];
}

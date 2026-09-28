using Axiomarium.Core.Assets;
using Axiomarium.Core.Manifests;
using Axiomarium.Core.Registry;

namespace Axiomarium.Core.Health;

/// <summary>The maturity a valid manifest claims, and what the manifest itself says about its evals.</summary>
/// <param name="File">The manifest, relative to the vault root.</param>
/// <param name="Location">Where its <c>maturity</c> is.</param>
/// <param name="Kind">The asset's kind.</param>
/// <param name="Folder">The asset's folder, as usage entries link to it.</param>
/// <param name="Maturity">The level it claims.</param>
/// <param name="TrueEvals">The eval types whose flag is true.</param>
internal sealed record MaturityClaim(
    string File, SourceLocation? Location, AssetKind Kind, string Folder, string Maturity, IReadOnlySet<string> TrueEvals);

/// <summary>Checks that each asset has the evidence its maturity requires in <see cref="MaturityRegistry"/>.</summary>
internal static class MaturityCheck
{
    /// <summary>Adds an error for each claim whose level needs evidence the asset doesn't have.</summary>
    /// <remarks>An eval type counts once its flag is true; the reference check makes sure its files exist.</remarks>
    public static void Examine(IEnumerable<MaturityClaim> claims, IReadOnlyList<UsageEntry> usage, List<Diagnostic> diagnostics)
    {
        foreach (var claim in claims)
        {
            var entries = usage.Where(entry => entry.Assets.Contains(claim.Folder)).ToList();
            var claimed = MaturityRegistry.Levels.Single(level => level.Name == claim.Maturity);
            var missing = Missing(claimed, claim, entries);
            if (missing.Count == 0)
            {
                continue;
            }

            // The first level has no requirements, so some level is always supported.
            var supported = MaturityRegistry.Levels.TakeWhile(level => Missing(level, claim, entries).Count == 0).Last();
            diagnostics.Add(new Diagnostic(
                Severity.Error,
                claim.File,
                claim.Location,
                $"maturity \"{claim.Maturity}\" lacks its evidence",
                [.. missing, $"The evidence supports {supported.Name}. Lower the maturity, or add the evidence."]));
        }
    }

    private static List<string> Missing(MaturityLevel level, MaturityClaim claim, List<UsageEntry> entries)
    {
        var missing = new List<string>();
        if (entries.Count < level.Usage)
        {
            missing.Add($"Needs {Count(level.Usage, "usage entry", "usage entries")} in {UsageLog.File}, found {entries.Count}.");
        }

        var repos = entries.Select(entry => entry.Repo).Where(repo => repo.Length > 0).Distinct(StringComparer.Ordinal).Count();
        if (repos < level.Repos)
        {
            missing.Add($"Needs {Count(level.Repos, "repo", "repos")} in its usage entries, found {repos}.");
        }

        var evals = claim.Kind == AssetKind.Skill ? level.Evals.Concat(level.SkillEvals) : level.Evals;
        foreach (var type in evals.Where(type => !claim.TrueEvals.Contains(type)))
        {
            missing.Add($"Needs {type} evals, and evals.{type} isn't true.");
        }

        return missing;
    }

    private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";
}

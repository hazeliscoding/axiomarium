using Axiomarium.Core.Assets;
using Axiomarium.Core.Health;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Core.Evals;

/// <summary>One session of an eval run: a case of an asset, on a harness, for the nth time.</summary>
/// <param name="Asset">The asset under test.</param>
/// <param name="Case">The case.</param>
/// <param name="CaseFolder">The case's folder, absolute, whose <c>repo/</c> the session starts in when it has one.</param>
/// <param name="Harness">The harness.</param>
/// <param name="Run">Which run of the case on this harness, from 1.</param>
public sealed record EvalSessionSpec(DiscoveredAsset Asset, EvalCase Case, string CaseFolder, Harness Harness, int Run)
{
    /// <summary>
    /// For <c>axm eval compare</c>, which version of the asset this session installs, or <see langword="null"/> for the
    /// asset as it is in the vault.
    /// </summary>
    public EvalVariant? Variant { get; init; }
}

/// <summary>A version of an asset that <c>axm eval compare</c> runs: the baseline or the candidate.</summary>
/// <param name="Name">Which one: <c>baseline</c> or <c>candidate</c>.</param>
/// <param name="Asset">The asset as that version's manifest says, or <see langword="null"/> for a baseline without the asset.</param>
/// <param name="VaultRoot">The folder that version's asset folder is in, or <see langword="null"/> when there's no asset.</param>
public sealed record EvalVariant(string Name, DiscoveredAsset? Asset, string? VaultRoot);

/// <summary>What <c>axm eval run</c> runs.</summary>
/// <param name="RepoRoot">The repo the vault is in, whose <c>.axm/evals/</c> keeps the run's history.</param>
/// <param name="VaultRoot">The vault the assets are in.</param>
/// <param name="Sessions">Every session: by asset folder, then harness, case and run.</param>
/// <param name="Notes">Assets left out, and why, as sentences.</param>
public sealed record EvalRunPlan(string RepoRoot, string VaultRoot, IReadOnlyList<EvalSessionSpec> Sessions, IReadOnlyList<string> Notes);

/// <summary>An eval run plan, or why there can't be one.</summary>
/// <param name="Plan">The plan, or <see langword="null"/>.</param>
/// <param name="Problem">Why there's no plan, as one line, or <see langword="null"/>.</param>
/// <param name="Hint">What to do about it, or <see langword="null"/>.</param>
public sealed record EvalRunSetup(EvalRunPlan? Plan, string? Problem, string? Hint);

/// <summary>Plans an eval run from the vault's eval cases. Reads, never writes.</summary>
public static class EvalRuns
{
    /// <summary>Plans a run of <paramref name="assets"/>' cases, or of every asset that has cases.</summary>
    /// <param name="folder">Where to start. The repo root is the nearest folder at or above it with a <c>.git</c>, and the vault is this folder or that root.</param>
    /// <param name="assets">The assets to run, each by name or, when two share a name, by folder such as <c>skills/deploy</c>. None for every asset with cases.</param>
    /// <param name="harnesses">The harnesses to run on. An asset runs only on those its manifest supports and <c>axm eval</c> can install it for.</param>
    /// <param name="runs">How many times each case runs on each harness.</param>
    /// <param name="machine">Where the harnesses' user files are.</param>
    /// <returns>
    /// The plan, or the problem: the folder doesn't exist, there's no vault, a named asset doesn't exist, shares its
    /// name, has no cases or can't be installed, no asset has cases, a case or manifest has errors, or no asset
    /// supports the harnesses asked for.
    /// </returns>
    public static EvalRunSetup Plan(string folder, IReadOnlyList<string> assets, IReadOnlyList<Harness> harnesses, int runs, Machine machine)
    {
        var start = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Directory.Exists(start))
        {
            return new EvalRunSetup(null, $"The folder {start} does not exist.", null);
        }

        var repoRoot = Instructions.Paths.Upward(start, machine.FileSystemRoot).FirstOrDefault(directory => Path.Exists(Path.Combine(directory, ".git"))) ?? start;
        var (vaultRoot, vault) = new[] { start, repoRoot }
            .Select(candidate => (Root: candidate, Report: Doctor.Run(candidate).Report))
            .FirstOrDefault(candidate => candidate.Report is not null);
        if (vault is null)
        {
            return new EvalRunSetup(null, $"No vault found in {start} or its repo.", "Run axm eval inside a vault, or pass --root <dir>.");
        }

        var named = new List<DiscoveredAsset>();
        foreach (var name in assets)
        {
            var matches = vault.Assets.Where(asset => name.Contains('/') ? asset.Folder == name : asset.Name == name).ToList();
            if (matches.Count == 0)
            {
                var names = vault.Assets.Select(asset => asset.Name).Distinct().Order(StringComparer.Ordinal);
                return new EvalRunSetup(null, $"The vault has no asset named {name}.", $"Its assets: {string.Join(", ", names)}.");
            }

            if (matches.Count > 1)
            {
                var folders = matches.Select(asset => asset.Folder).Order(StringComparer.Ordinal).ToList();
                return new EvalRunSetup(null, $"Two assets are named {name}: {string.Join(" and ", folders)}.", $"Name one by its folder, such as {folders[^1]}.");
            }

            named.Add(matches[0]);
        }

        var sessions = new List<EvalSessionSpec>();
        var notes = new List<string>();
        var anyCases = false;
        foreach (var asset in (named.Count > 0 ? named : vault.Assets).DistinctBy(asset => asset.Folder).OrderBy(asset => asset.Folder, StringComparer.Ordinal))
        {
            var (cases, problem) = Cases(vaultRoot!, asset);
            if (problem is not null)
            {
                return new EvalRunSetup(null, problem, "Run axm validate to see them.");
            }

            if (cases.Count == 0)
            {
                if (named.Count > 0)
                {
                    return new EvalRunSetup(null, $"{asset.Name} has no eval cases yet.", $"Add one as {asset.Folder}/evals/behavioral/<case>/eval.yaml.");
                }

                continue;
            }

            anyCases = true;
            if (asset.Manifest is null)
            {
                return new EvalRunSetup(null, $"{asset.Folder}/asset.yaml has errors.", "Run axm validate to see them.");
            }

            if (EvalInstall.Refusal(asset.Kind, Harness.ClaudeCode) is { } refusal && EvalInstall.Refusal(asset.Kind, Harness.Codex) is not null)
            {
                if (named.Count > 0)
                {
                    return new EvalRunSetup(null, refusal, null);
                }

                notes.Add($"{asset.Folder} has eval cases, but {refusal}");
                continue;
            }

            foreach (var harness in harnesses.Where(harness => asset.Manifest.Supports.ContainsKey(harness.Name()) && EvalInstall.Refusal(asset.Kind, harness) is null))
            {
                sessions.AddRange(cases.SelectMany(found => Enumerable.Range(1, runs).Select(run => new EvalSessionSpec(asset, found.Case, found.Folder, harness, run))));
            }
        }

        if (!anyCases)
        {
            return new EvalRunSetup(null, "No asset in the vault has eval cases yet.", "Add one as <kind folder>/<asset>/evals/behavioral/<case>/eval.yaml.");
        }

        return sessions.Count == 0
            ? new EvalRunSetup(null, $"None of the assets with eval cases supports {string.Join(" or ", harnesses.Select(harness => harness.Name()))}.", null)
            : new EvalRunSetup(new EvalRunPlan(repoRoot, vaultRoot!, sessions, notes), null, null);
    }

    // An asset's cases, behavioral then regression, each in folder order, or the first case that can't be read.
    private static (List<(EvalCase Case, string Folder)> Cases, string? Problem) Cases(string vaultRoot, DiscoveredAsset asset)
    {
        var cases = new List<(EvalCase, string)>();
        foreach (var type in Enum.GetValues<EvalType>())
        {
            var directory = Path.Combine(vaultRoot, asset.Folder.Replace('/', Path.DirectorySeparatorChar), "evals", EvalCases.Folder(type));
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var caseFolder in Directory.EnumerateDirectories(directory).Where(path => !Path.GetFileName(path).StartsWith('.')).Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(caseFolder);
                var file = Path.Combine(caseFolder, EvalCases.FileName);
                var shown = $"{asset.Folder}/evals/{EvalCases.Folder(type)}/{name}/{EvalCases.FileName}";
                if (!File.Exists(file))
                {
                    return ([], $"{shown} is missing.");
                }

                var read = EvalCases.Read(File.ReadAllText(file), name, type);
                if (read.Case is null)
                {
                    return ([], $"{shown} has errors.");
                }

                cases.Add((read.Case, caseFolder));
            }
        }

        return (cases, null);
    }
}

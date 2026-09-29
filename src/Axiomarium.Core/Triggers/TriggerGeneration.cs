using Axiomarium.Core.Assets;
using Axiomarium.Core.Health;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Core.Triggers;

/// <summary>What <c>axm triggers generate</c> needs to ask for a vault skill's prompts.</summary>
/// <param name="RepoRoot">The repo root the harnesses launch from, which paths are shown from.</param>
/// <param name="Skill">The skill's name.</param>
/// <param name="Text">Its description and <c>use_when</c>, as sync would list them.</param>
/// <param name="Rivals">The listed skills it overlaps most, with the text the model sees.</param>
/// <param name="Target">Its absolute <c>evals/trigger/prompts.yaml</c>, which may not exist yet.</param>
/// <param name="Manifest">Its absolute <c>asset.yaml</c>.</param>
public sealed record GenerationPlan(string RepoRoot, string Skill, string Text, IReadOnlyList<(string Name, string Text)> Rivals, string Target, string Manifest);

/// <summary>A generation plan, or why there can't be one.</summary>
/// <param name="Plan">The plan, or <see langword="null"/>.</param>
/// <param name="Problem">Why there's no plan, as one line, or <see langword="null"/>.</param>
/// <param name="Hint">What to do about it, or <see langword="null"/>.</param>
public sealed record GenerationSetup(GenerationPlan? Plan, string? Problem, string? Hint);

/// <summary>Finds the vault skill <c>axm triggers generate</c> writes prompts for. Reads, never writes.</summary>
public static class TriggerGeneration
{
    /// <summary>Plans prompts for <paramref name="skill"/>.</summary>
    /// <param name="folder">Where to start. The vault is this folder or its repo root, as for <see cref="TriggerOverlap.Check"/>.</param>
    /// <param name="skill">The vault skill's name.</param>
    /// <param name="machine">Where the harnesses' user files are, for finding rivals in each listing.</param>
    /// <returns>
    /// The plan, or the problem: the folder doesn't exist, there's no vault, the vault has no such skill, or
    /// the skill's manifest has errors, so its description can't be trusted.
    /// </returns>
    public static GenerationSetup Plan(string folder, string skill, Machine machine)
    {
        var overlap = TriggerOverlap.Check(folder, machine);
        if (overlap.Report is not { } report)
        {
            return new GenerationSetup(null, overlap.Problem, null);
        }

        var start = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (report.VaultRoot is not { } vaultRoot || Doctor.Run(vaultRoot).Report is not { } vault)
        {
            return new GenerationSetup(null, $"No vault found in {start} or its repo.", "Run axm triggers generate inside a vault, or pass --root <dir>.");
        }

        var skills = vault.Assets.Where(asset => asset.Kind == AssetKind.Skill).ToList();
        if (skills.FirstOrDefault(asset => asset.Name == skill) is not { } found)
        {
            return new GenerationSetup(
                null,
                $"The vault has no skill named {skill}.",
                skills.Count == 0 ? "It has no skills yet." : $"Its skills: {string.Join(", ", skills.Select(asset => asset.Name))}.");
        }

        var manifest = Path.GetFullPath(Path.Combine(vaultRoot, $"{found.Folder}/asset.yaml"));
        if (found.Manifest is not { UseWhen: { } useWhen } fields || vault.Diagnostics.Any(diagnostic => diagnostic.Severity == Severity.Error && diagnostic.File == $"{found.Folder}/asset.yaml"))
        {
            return new GenerationSetup(null, $"{found.Folder}/asset.yaml has errors, so its description can't be read.", "Run axm validate to see them.");
        }

        var target = Path.GetFullPath(Path.Combine(vaultRoot, $"{found.Folder}/{TriggerPrompts.RelativePath}"));
        return new GenerationSetup(
            new GenerationPlan(report.RepoRoot, skill, $"{fields.Description} - {useWhen}", TriggerOverlap.Rivals(report, skill), target, manifest),
            null,
            null);
    }
}

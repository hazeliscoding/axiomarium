using Axiomarium.Core.Assets;
using Axiomarium.Core.Health;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Core.Triggers;

/// <summary>A skill compared for overlap, as one harness lists it.</summary>
/// <param name="Name">The name the listing shows, or a vault skill's asset name, which sync would list it by.</param>
/// <param name="Path">Its absolute <c>SKILL.md</c> or command file, or a vault skill's <c>asset.yaml</c>.</param>
/// <param name="FromVault">Whether it's a vault skill, compared on its <c>description</c> and <c>skill.use_when</c> as sync would list it.</param>
public sealed record ComparedSkill(string Name, string Path, bool FromVault);

/// <summary>Two skills in one listing whose text shares terms that are rare in that listing.</summary>
/// <param name="First">The skill listed first.</param>
/// <param name="Second">The skill listed after it.</param>
/// <param name="Score">The cosine similarity of the two texts' term weights, from 0 to 1, at least <see cref="TriggerOverlap.Threshold"/>.</param>
/// <param name="Shared">Up to five shared terms, the heaviest first, each as <paramref name="First"/> writes it.</param>
public sealed record SkillOverlap(ComparedSkill First, ComparedSkill Second, double Score, IReadOnlyList<string> Shared);

/// <summary>What one harness's listing at the repo root holds, and the pairs in it that overlap.</summary>
/// <param name="Harness">The harness.</param>
/// <param name="Compared">Every skill compared, in listing order, with the vault's skills in place of listed ones of the same name and after the rest.</param>
/// <param name="LeftOut">How many skills built into the harness are left out, because their text isn't recorded.</param>
/// <param name="Repeats">How many listed skills are left out because one listed before them has their name, which <c>skill-name-clash</c> reports.</param>
/// <param name="Pairs">The pairs at or above the threshold, the highest score first.</param>
public sealed record HarnessOverlap(Harness Harness, IReadOnlyList<ComparedSkill> Compared, int LeftOut, int Repeats, IReadOnlyList<SkillOverlap> Pairs);

/// <summary>The overlap in each harness's skill listing.</summary>
/// <param name="RepoRoot">The repo root the harnesses were launched from.</param>
/// <param name="VaultRoot">The vault whose skills were added, or <see langword="null"/> when there is none.</param>
/// <param name="Harnesses">Claude Code, then Codex.</param>
public sealed record TriggerReport(string RepoRoot, string? VaultRoot, IReadOnlyList<HarnessOverlap> Harnesses);

/// <summary>A trigger report, or why there is none.</summary>
/// <param name="Report">The report, or <see langword="null"/> when the check couldn't run.</param>
/// <param name="Problem">Why it couldn't run, or <see langword="null"/>.</param>
public sealed record TriggerResult(TriggerReport? Report, string? Problem);

/// <summary>
/// Finds skills whose descriptions overlap, in the text each harness shows the model, without a model. A
/// term weighs more the rarer it is in the listing, so words every skill uses count for little. Overlap is
/// shared wording, not proof that the model confuses the two skills.
/// </summary>
public static class TriggerOverlap
{
    /// <summary>The lowest score reported as overlap.</summary>
    public const double Threshold = 0.15;

    private const int SharedShown = 5;

    /// <summary>
    /// Compares the skills each harness lists when launched at the repo root, with the vault's skills added
    /// to each harness their manifest supports. Reads, never writes.
    /// </summary>
    /// <param name="folder">Where to start: the repo root is the nearest folder at or above it with a <c>.git</c>, or the folder itself. The vault is the folder or the repo root, whichever holds one first.</param>
    /// <param name="machine">Where the harnesses' user and managed files are.</param>
    /// <returns>The report, or the problem when <paramref name="folder"/> doesn't exist or is a file.</returns>
    public static TriggerResult Check(string folder, Machine machine)
    {
        var start = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (File.Exists(start))
        {
            return new TriggerResult(null, $"{start} is a file, not a folder.");
        }

        if (!Directory.Exists(start))
        {
            return new TriggerResult(null, $"The folder {start} does not exist.");
        }

        var repoRoot = Instructions.Paths.Upward(start, machine.FileSystemRoot).FirstOrDefault(directory => Path.Exists(Path.Combine(directory, ".git"))) ?? start;
        var (vaultRoot, vault) = new[] { start, repoRoot }
            .Select(candidate => (Root: candidate, Report: Doctor.Run(candidate).Report))
            .FirstOrDefault(candidate => candidate.Report is not null);
        var vaultSkills = vault?.Assets
            .Where(asset => asset.Kind == AssetKind.Skill && asset.Manifest is { UseWhen: not null })
            .ToList() ?? [];

        Harness[] harnesses = [Harness.ClaudeCode, Harness.Codex];
        var explanation = Explainer.Explain(Path.Combine(repoRoot, "axm-probe"), repoRoot, harnesses, machine, repoRoot, repoRoot: repoRoot);
        var overlaps = new List<HarnessOverlap>();
        foreach (var harness in explanation.Harnesses)
        {
            var skills = harness.Resolution.Skills;
            var listed = skills.Where(skill => skill.Path is not null).ToList();

            // A name listed twice is a clash the doctor reports; comparing each copy would repeat every pair.
            var texts = listed.DistinctBy(skill => skill.Name, StringComparer.Ordinal)
                .Select(skill => (Skill: new ComparedSkill(skill.Name, skill.Path!, false), Text: skill.Text ?? "")).ToList();
            var repeats = listed.Count - texts.Count;
            foreach (var asset in vaultSkills.Where(asset => asset.Manifest!.Supports.ContainsKey(harness.Harness.Name())))
            {
                var synced = (Skill: new ComparedSkill(asset.Name, Path.GetFullPath(Path.Combine(vaultRoot!, asset.ManifestFile!)), true), Text: $"{asset.Manifest!.Description} - {asset.Manifest.UseWhen}");
                var at = texts.FindIndex(text => text.Skill.Name == asset.Name);
                texts.RemoveAll(text => text.Skill.Name == asset.Name);
                texts.Insert(at < 0 ? texts.Count : at, synced);
            }

            overlaps.Add(new HarnessOverlap(harness.Harness, [.. texts.Select(text => text.Skill)], skills.Count(skill => skill.Path is null), repeats, Compare(texts)));
        }

        return new TriggerResult(new TriggerReport(repoRoot, vault is null ? null : vaultRoot, overlaps), null);
    }

    // TF-IDF cosine: a term's weight grows slowly with its count in the text, and with how few texts in the
    // listing use it. The IDF is smoothed, so a term two skills of two share still counts. A name counts
    // without its namespace, such as a plugin's, which every skill of that plugin shares by construction.
    private static List<SkillOverlap> Compare(List<(ComparedSkill Skill, string Text)> texts)
    {
        var terms = texts.Select(text => Terms.Of($"{text.Text} {text.Skill.Name[(text.Skill.Name.LastIndexOf(':') + 1)..]}")
            .GroupBy(term => term.Stem)
            .ToDictionary(group => group.Key, group => (Count: group.Count(), group.First().Word), StringComparer.Ordinal)).ToList();
        var used = terms.SelectMany(text => text.Keys).GroupBy(stem => stem, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var weights = terms.Select(text => text.ToDictionary(
            term => term.Key, term => (1 + Math.Log(term.Value.Count)) * Math.Log(1 + (double)texts.Count / used[term.Key]), StringComparer.Ordinal)).ToList();
        var norms = weights.Select(weight => Math.Sqrt(weight.Values.Sum(value => value * value))).ToList();

        var pairs = new List<SkillOverlap>();
        for (var i = 0; i < texts.Count; i++)
        {
            for (var j = i + 1; j < texts.Count; j++)
            {
                var shared = weights[i].Keys.Where(weights[j].ContainsKey).Select(stem => (Stem: stem, Weight: weights[i][stem] * weights[j][stem])).ToList();
                var score = shared.Sum(term => term.Weight) / (norms[i] * norms[j]);
                if (shared.Count == 0 || score < Threshold)
                {
                    continue;
                }

                var words = shared.Select(term => (Word: terms[i][term.Stem].Word, term.Weight))
                    .OrderByDescending(term => term.Weight).ThenBy(term => term.Word, StringComparer.Ordinal)
                    .Take(SharedShown).Select(term => term.Word);
                pairs.Add(new SkillOverlap(texts[i].Skill, texts[j].Skill, score, [.. words]));
            }
        }

        return [.. pairs.OrderByDescending(pair => pair.Score)];
    }
}

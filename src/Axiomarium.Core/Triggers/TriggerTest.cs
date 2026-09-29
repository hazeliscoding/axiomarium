using Axiomarium.Core.Assets;
using Axiomarium.Core.Health;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Core.Triggers;

/// <summary>What <c>axm triggers test</c> runs.</summary>
/// <param name="RepoRoot">The repo root the harnesses launch from.</param>
/// <param name="Prompts">The prompt file of each skill under test, by name.</param>
/// <param name="Workspace">What the test's throwaway copy of the repo holds.</param>
/// <param name="Sessions">Every session: by skill, then harness, prompt and run.</param>
public sealed record TriggerTestPlan(string RepoRoot, IReadOnlyDictionary<string, TriggerPromptFile> Prompts, WorkspacePlan Workspace, IReadOnlyList<TriggerSession> Sessions);

/// <summary>A trigger test plan, or why there can't be one.</summary>
/// <param name="Plan">The plan, or <see langword="null"/>.</param>
/// <param name="Problem">Why there's no plan, as one line, or <see langword="null"/>.</param>
/// <param name="Hint">What to do about it, or <see langword="null"/>.</param>
public sealed record TriggerTestSetup(TriggerTestPlan? Plan, string? Problem, string? Hint);

/// <summary>Plans a trigger test from the vault's prompt files. Reads, never writes.</summary>
public static class TriggerTest
{
    /// <summary>Plans a test of <paramref name="skills"/>, or of every vault skill that has prompts.</summary>
    /// <param name="folder">Where to start. The repo root is the nearest folder at or above it with a <c>.git</c>, and the vault is this folder or that root.</param>
    /// <param name="skills">The vault skills to test, or none for every one that has prompts.</param>
    /// <param name="harnesses">The harnesses to test on. A skill runs only on those its manifest supports.</param>
    /// <param name="runs">How many times each prompt runs on each harness.</param>
    /// <param name="machine">Where the harnesses' user files are, for the instruction files that load at launch.</param>
    /// <returns>
    /// The plan, or the problem: the folder doesn't exist, there's no vault, a named skill doesn't exist or has
    /// no prompts, no skill has prompts, a prompt file has errors, or no skill supports the harnesses asked for.
    /// </returns>
    public static TriggerTestSetup Plan(string folder, IReadOnlyList<string> skills, IReadOnlyList<Harness> harnesses, int runs, Machine machine)
    {
        var start = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Directory.Exists(start))
        {
            return new TriggerTestSetup(null, $"The folder {start} does not exist.", null);
        }

        var repoRoot = Instructions.Paths.Upward(start, machine.FileSystemRoot).FirstOrDefault(directory => Path.Exists(Path.Combine(directory, ".git"))) ?? start;
        var (vaultRoot, vault) = new[] { start, repoRoot }
            .Select(candidate => (Root: candidate, Report: Doctor.Run(candidate).Report))
            .FirstOrDefault(candidate => candidate.Report is not null);
        if (vault is null)
        {
            return new TriggerTestSetup(null, $"No vault found in {start} or its repo.", "Run axm triggers test inside a vault, or pass --root <dir>.");
        }

        var assets = vault.Assets.Where(asset => asset.Kind == AssetKind.Skill).ToList();
        if (skills.FirstOrDefault(name => assets.All(asset => asset.Name != name)) is { } unknown)
        {
            return new TriggerTestSetup(null, $"The vault has no skill named {unknown}.", $"Its skills: {string.Join(", ", assets.Select(asset => asset.Name))}.");
        }

        var prompts = new SortedDictionary<string, (TriggerPromptFile File, IReadOnlyDictionary<string, string> Supports)>(StringComparer.Ordinal);
        foreach (var asset in assets.Where(asset => skills.Count == 0 || skills.Contains(asset.Name)))
        {
            var path = Path.Combine(vaultRoot!, asset.Folder, TriggerPrompts.RelativePath);
            if (!File.Exists(path))
            {
                if (skills.Contains(asset.Name))
                {
                    return new TriggerTestSetup(null, $"{asset.Name} has no trigger prompts yet.", $"Run axm triggers generate {asset.Name} first.");
                }

                continue;
            }

            if (TriggerPrompts.Read(File.ReadAllText(path), asset.Name).File is not { } file || asset.Manifest is null)
            {
                return new TriggerTestSetup(null, $"{asset.Folder}/{TriggerPrompts.RelativePath} has errors.", "Run axm validate to see them.");
            }

            prompts[asset.Name] = (file, asset.Manifest.Supports);
        }

        if (prompts.Count == 0)
        {
            return new TriggerTestSetup(null, "No vault skill has trigger prompts yet.", "Run axm triggers generate <skill> first.");
        }

        var sessions = (
            from skill in prompts
            from harness in harnesses.Where(harness => skill.Value.Supports.ContainsKey(harness.Name())).Order()
            from prompt in skill.Value.File.Prompts.Select((prompt, index) => (prompt, index))
            from run in Enumerable.Range(1, runs)
            select new TriggerSession(harness, skill.Key, prompt.index, run, prompt.prompt.Prompt)).ToList();
        if (sessions.Count == 0)
        {
            return new TriggerTestSetup(null, $"None of the skills with prompts supports {string.Join(" or ", harnesses.Select(harness => harness.Name()))}.", null);
        }

        return new TriggerTestSetup(
            new TriggerTestPlan(repoRoot, prompts.ToDictionary(skill => skill.Key, skill => skill.Value.File, StringComparer.Ordinal), TriggerWorkspace.Plan(repoRoot, vaultRoot, machine), sessions),
            null,
            null);
    }
}

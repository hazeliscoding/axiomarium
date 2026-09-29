using Axiomarium.Core.Assets;
using Axiomarium.Core.Health;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Core.Triggers;

/// <summary>A file to write into a trigger test's throwaway copy.</summary>
/// <param name="Path">Where, relative to the copy's root, with forward slashes.</param>
/// <param name="Content">What, with <c>\n</c> line ends.</param>
public sealed record WorkspaceFile(string Path, string Content);

/// <summary>What a trigger test's throwaway copy of the repo holds.</summary>
/// <param name="Copy">The repo's files to copy, relative to its root, with forward slashes, in ordinal order.</param>
/// <param name="Write">The vault's skills as each harness's skill files, in ordinal order of their paths.</param>
public sealed record WorkspacePlan(IReadOnlyList<string> Copy, IReadOnlyList<WorkspaceFile> Write);

/// <summary>
/// Plans the throwaway copy <c>axm triggers test</c> launches the harnesses in. Each session stops at its first
/// action, so the copy only needs what shapes the skill listing and the instructions at launch. Reads, never writes.
/// </summary>
public static class TriggerWorkspace
{
    private static readonly string[] Folders = [".claude", ".agents", ".codex", ".claude-plugin", ".codex-plugin", ".cursor-plugin"];
    private static readonly string[] RootFiles = ["CLAUDE.md", "CLAUDE.local.md", "AGENTS.md", "AGENTS.override.md", ".mcp.json"];

    /// <summary>Plans the copy.</summary>
    /// <param name="repoRoot">The repo root, where the harnesses would launch.</param>
    /// <param name="vaultRoot">The vault whose skills are written in, or <see langword="null"/> when there's none.</param>
    /// <param name="machine">Where the harnesses' user files are, for the instruction files that load at launch.</param>
    /// <returns>
    /// The root instruction files, each harness's own folders, and every in-repo file the harnesses load at launch,
    /// such as an import; and each valid vault skill as the skill file of each harness it supports, in place of
    /// any copy of it already there.
    /// </returns>
    public static WorkspacePlan Plan(string repoRoot, string? vaultRoot, Machine machine)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
        string Relative(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

        var copy = new SortedSet<string>(StringComparer.Ordinal);
        copy.UnionWith(RootFiles.Where(name => File.Exists(Path.Combine(root, name))));
        foreach (var folder in Folders.Select(name => Path.Combine(root, name)).Where(Directory.Exists))
        {
            copy.UnionWith(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(Relative).Where(path => !path.Split('/').Contains(".git")));
        }

        Harness[] harnesses = [Harness.ClaudeCode, Harness.Codex];
        var explanation = Explainer.Explain(Path.Combine(root, "axm-probe"), root, harnesses, machine, root, repoRoot: root);
        copy.UnionWith(explanation.Harnesses
            .SelectMany(harness => harness.Resolution.Loaded)
            .Where(item => item.Timing == LoadTiming.AtLaunch && Instructions.Paths.IsUnder(item.Path, root))
            .Select(item => Relative(item.Path)));

        var write = new List<WorkspaceFile>();
        var vault = vaultRoot is null ? null : Doctor.Run(vaultRoot).Report;
        foreach (var asset in vault?.Assets.Where(asset => asset.Kind == AssetKind.Skill && asset.Manifest is { UseWhen: not null }) ?? [])
        {
            var manifest = asset.Manifest!;
            var content = Path.Combine(vaultRoot!, asset.Folder, asset.Kind.ContentFile());
            var body = File.Exists(content) ? File.ReadAllText(content).Replace("\r\n", "\n", StringComparison.Ordinal) : "";
            if (manifest.Supports.ContainsKey(Harness.ClaudeCode.Name()))
            {
                var folder = $".claude/skills/{asset.Name}/";
                copy.RemoveWhere(path => path.StartsWith(folder, StringComparison.Ordinal));
                write.Add(new WorkspaceFile(
                    folder + "SKILL.md",
                    $"---\nname: {asset.Name}\ndescription: {TriggerPrompts.Quoted(manifest.Description)}\nwhen_to_use: {TriggerPrompts.Quoted(manifest.UseWhen!)}\n---\n{body}"));
            }

            // Codex has no when_to_use, so both go in the description, as sync would list them.
            if (manifest.Supports.ContainsKey(Harness.Codex.Name()))
            {
                var folder = $".agents/skills/{asset.Name}/";
                copy.RemoveWhere(path => path.StartsWith(folder, StringComparison.Ordinal));
                write.Add(new WorkspaceFile(
                    folder + "SKILL.md",
                    $"---\nname: {asset.Name}\ndescription: {TriggerPrompts.Quoted($"{manifest.Description} - {manifest.UseWhen}")}\n---\n{body}"));
            }
        }

        return new WorkspacePlan([.. copy], [.. write.OrderBy(file => file.Path, StringComparer.Ordinal)]);
    }
}

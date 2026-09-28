using System.Text.Json.Nodes;
using Axiomarium.Core.Manifests;

namespace Axiomarium.Core.Registry;

/// <summary>A maturity level: what it promises, and the evidence an asset needs before it claims it.</summary>
/// <param name="Name">The level, as a manifest's <c>maturity</c> names it.</param>
/// <param name="Promise">What the level promises to someone using the asset.</param>
/// <param name="Usage">How many usage entries must link to the asset.</param>
/// <param name="Repos">How many different repos those entries must name.</param>
/// <param name="Evals">The eval types every asset needs, such as <c>behavioral</c>.</param>
/// <param name="SkillEvals">The eval types a skill needs on top of <paramref name="Evals"/>, such as <c>trigger</c>.</param>
public sealed record MaturityLevel(string Name, string Promise, int Usage, int Repos, IReadOnlyList<string> Evals, IReadOnlyList<string> SkillEvals);

/// <summary>The maturity levels from the repo's <c>registry/maturity.yaml</c>, embedded in the binary.</summary>
/// <remarks>
/// Every vault is judged by the same promises, so the levels come from the binary, not from the vault
/// being checked. Tests keep the embedded copy equal to the repo's file and valid against its schema.
/// </remarks>
public static class MaturityRegistry
{
    private const string Resource = "registry/maturity.yaml";
    private static readonly Lazy<IReadOnlyList<MaturityLevel>> Loaded = new(Load);

    /// <summary>Every level, from the least trusted to the most.</summary>
    public static IReadOnlyList<MaturityLevel> Levels => Loaded.Value;

    private static IReadOnlyList<MaturityLevel> Load()
    {
        using var stream = typeof(MaturityRegistry).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The binary is missing its embedded {Resource}.");
        using var reader = new StreamReader(stream);
        var parsed = YamlDocument.Parse(reader.ReadToEnd());
        if (parsed.Root?["levels"] is not JsonArray levels)
        {
            throw new InvalidOperationException($"The embedded {Resource} has no levels: {parsed.Problem?.Message}");
        }

        return [.. levels.Select(level => new MaturityLevel(
            level!["name"]!.GetValue<string>(),
            level["promise"]!.GetValue<string>(),
            Number(level, "usage"),
            Number(level, "repos"),
            Names(level, "evals"),
            Names(level, "skill_evals")))];
    }

    private static int Number(JsonNode level, string field) => level[field] is JsonValue value ? value.GetValue<int>() : 0;

    private static IReadOnlyList<string> Names(JsonNode level, string field) =>
        level[field] is JsonArray names ? [.. names.Select(name => name!.GetValue<string>())] : [];
}

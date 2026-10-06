using System.Text.Json.Nodes;
using Axiomarium.Core.Assets;

namespace Axiomarium.Core.Schemas;

/// <summary>The schemas in the repo's <c>schemas/</c> folder, embedded in the binary.</summary>
public static class SchemaCatalog
{
    private static readonly Lazy<JsonObject> AssetSchema = new(() => Load("schemas/asset.schema.json"));
    private static readonly Lazy<JsonObject> SkillSchema = new(() => Load("schemas/skill.schema.json"));
    private static readonly Lazy<JsonObject> HookSchema = new(() => Load("schemas/hook.schema.json"));
    private static readonly Lazy<JsonObject> PolicySchema = new(() => Load("schemas/policy.schema.json"));
    private static readonly Lazy<JsonObject> RepoSchema = new(() => Load("schemas/axiomarium.schema.json"));
    private static readonly Lazy<JsonObject> TriggerPromptsSchema = new(() => Load("schemas/trigger-prompts.schema.json"));
    private static readonly Lazy<JsonObject> EvalSchema = new(() => Load("schemas/eval.schema.json"));
    private static readonly Lazy<JsonObject> EvidenceSchema = new(() => Load("schemas/evidence.schema.json"));

    /// <summary>The schema every <c>asset.yaml</c> must satisfy.</summary>
    public static JsonObject Asset => AssetSchema.Value;

    /// <summary>The schema a repo's <c>axiomarium.yaml</c> must satisfy.</summary>
    public static JsonObject Repo => RepoSchema.Value;

    /// <summary>The schema a skill's <c>evals/trigger/prompts.yaml</c> must satisfy.</summary>
    public static JsonObject TriggerPrompts => TriggerPromptsSchema.Value;

    /// <summary>The schema an asset's eval case, <c>evals/behavioral/&lt;case&gt;/eval.yaml</c> or <c>evals/regression/&lt;case&gt;/eval.yaml</c>, must satisfy.</summary>
    public static JsonObject Eval => EvalSchema.Value;

    /// <summary>The schema an evidence record, <c>.axm/evidence/&lt;check&gt;.json</c>, must satisfy.</summary>
    public static JsonObject Evidence => EvidenceSchema.Value;

    /// <summary>The schema for the block named after <paramref name="kind"/> in its <c>asset.yaml</c>.</summary>
    /// <param name="kind">The asset's kind.</param>
    /// <returns>
    /// The block's schema for a skill, hook or policy, which must have its block. <see langword="null"/>
    /// for the other kinds, which have none.
    /// </returns>
    public static JsonObject? Block(AssetKind kind) => kind switch
    {
        AssetKind.Skill => SkillSchema.Value,
        AssetKind.Hook => HookSchema.Value,
        AssetKind.Policy => PolicySchema.Value,
        _ => null,
    };

    /// <summary>Reads an embedded schema by its path in the repo, such as <c>schemas/asset.schema.json</c>.</summary>
    internal static JsonObject Load(string name)
    {
        using var stream = typeof(SchemaCatalog).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The binary is missing its embedded {name}.");
        return JsonNode.Parse(stream)!.AsObject();
    }
}

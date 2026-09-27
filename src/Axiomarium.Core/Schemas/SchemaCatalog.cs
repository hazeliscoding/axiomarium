using System.Text.Json.Nodes;

namespace Axiomarium.Core.Schemas;

/// <summary>The schemas in the repo's <c>schemas/</c> folder, embedded in the binary.</summary>
public static class SchemaCatalog
{
    private static readonly Lazy<JsonObject> AssetSchema = new(() => Load("schemas/asset.schema.json"));

    /// <summary>The schema every <c>asset.yaml</c> must satisfy.</summary>
    public static JsonObject Asset => AssetSchema.Value;

    private static JsonObject Load(string name)
    {
        using var stream = typeof(SchemaCatalog).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The binary is missing its embedded {name}.");
        return JsonNode.Parse(stream)!.AsObject();
    }
}

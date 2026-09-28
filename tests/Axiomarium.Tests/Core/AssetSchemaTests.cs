using System.Text.Json.Nodes;
using Axiomarium.Core.Manifests;
using Axiomarium.Core.Schemas;

namespace Axiomarium.Tests.Core;

public class AssetSchemaTests
{
    private static IReadOnlyList<SchemaError> ValidateYaml(string yaml)
    {
        var parsed = YamlDocument.Parse(yaml);
        Assert.Null(parsed.Problem);
        return SchemaValidator.Validate(parsed.Root, SchemaCatalog.Asset);
    }

    [Fact]
    public void A_complete_manifest_is_valid()
    {
        Assert.Empty(ValidateYaml(SampleManifests.Valid));
    }

    [Fact]
    public void Unknown_maturity_names_the_allowed_values()
    {
        var error = Assert.Single(ValidateYaml(SampleManifests.Valid.Replace("maturity: experimental", "maturity: production-ready")));

        Assert.Equal("maturity", error.Path);
        Assert.Equal("Unknown maturity: \"production-ready\"", error.Message);
        Assert.Equal(["Allowed: experimental, incubating, tested, stable, battle-tested"], error.Detail);
    }

    [Fact]
    public void Numeric_version_says_to_quote_it()
    {
        var error = Assert.Single(ValidateYaml(SampleManifests.Valid.Replace("version: 0.1.0", "version: 1.0")));

        Assert.Equal("version must be a string, found a number", error.Message);
        Assert.Equal(["Quote it: \"1.0\""], error.Detail);
    }

    [Fact]
    public void Misspelled_field_is_unknown_and_the_real_one_missing()
    {
        var errors = ValidateYaml(SampleManifests.Valid.Replace("maturity: experimental", "maturty: experimental"));

        Assert.Equal(["Missing required field: maturity", "Unknown field: maturty"], errors.Select(e => e.Message));
    }

    public static TheoryData<string> SchemaFiles =>
        new(Directory.GetFiles(Path.Combine(RepoRoot.Path, "schemas"), "*.schema.json").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(SchemaFiles))]
    public void Schema_uses_only_supported_keywords(string file)
    {
        var used = new SortedSet<string>(StringComparer.Ordinal);
        CollectKeywords(SchemaCatalog.Load($"schemas/{file}"), used);

        Assert.Empty(used.Except(SchemaValidator.SupportedKeywords));
    }

    [Theory]
    [MemberData(nameof(SchemaFiles))]
    public void Embedded_schema_matches_the_file_in_the_repo(string file)
    {
        var onDisk = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot.Path, "schemas", file)));

        Assert.True(JsonNode.DeepEquals(onDisk, SchemaCatalog.Load($"schemas/{file}")));
    }

    // Keys of a schema object are keywords, except the names under properties and $defs, whose
    // values are schemas again.
    private static void CollectKeywords(JsonObject schema, ISet<string> used)
    {
        foreach (var (keyword, value) in schema)
        {
            used.Add(keyword);
            switch (keyword)
            {
                case "properties" or "$defs":
                    foreach (var (_, child) in value!.AsObject())
                    {
                        CollectKeywords(child!.AsObject(), used);
                    }

                    break;
                case "items":
                    CollectKeywords(value!.AsObject(), used);
                    break;
            }
        }
    }
}

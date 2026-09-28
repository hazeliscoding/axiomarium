using Axiomarium.Core.Manifests;
using Axiomarium.Core.Registry;
using Axiomarium.Core.Schemas;

namespace Axiomarium.Tests.Core;

public class MaturityRegistryTests
{
    private static string RegistryFile => Path.Combine(RepoRoot.Path, "registry", "maturity.yaml");

    [Fact]
    public void Levels_follow_the_asset_schemas_maturity_order()
    {
        var allowed = SchemaCatalog.Asset["properties"]!["maturity"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>());

        Assert.Equal(allowed, MaturityRegistry.Levels.Select(level => level.Name));
    }

    [Fact]
    public void The_registry_file_matches_its_schema()
    {
        var parsed = YamlDocument.Parse(File.ReadAllText(RegistryFile));

        Assert.Null(parsed.Problem);
        Assert.Empty(SchemaValidator.Validate(parsed.Root, SchemaCatalog.Load("schemas/maturity.schema.json")));
    }

    [Fact]
    public void Embedded_registry_matches_the_file_in_the_repo()
    {
        using var stream = typeof(MaturityRegistry).Assembly.GetManifestResourceStream("registry/maturity.yaml");
        using var reader = new StreamReader(stream!);

        Assert.Equal(File.ReadAllText(RegistryFile).ReplaceLineEndings("\n"), reader.ReadToEnd().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void A_level_carries_its_promise_and_the_evidence_it_needs()
    {
        var tested = MaturityRegistry.Levels.Single(level => level.Name == "tested");

        Assert.False(string.IsNullOrWhiteSpace(tested.Promise));
        Assert.Equal(1, tested.Usage);
        Assert.Equal(0, tested.Repos);
        Assert.Equal(["behavioral", "regression"], tested.Evals);
        Assert.Equal(["trigger"], tested.SkillEvals);
    }

    [Fact]
    public void Experimental_needs_no_evidence()
    {
        var experimental = MaturityRegistry.Levels[0];

        Assert.Equal("experimental", experimental.Name);
        Assert.Equal((0, 0), (experimental.Usage, experimental.Repos));
        Assert.Empty(experimental.Evals);
        Assert.Empty(experimental.SkillEvals);
    }
}

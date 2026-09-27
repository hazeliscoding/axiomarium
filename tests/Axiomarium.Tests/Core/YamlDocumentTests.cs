using Axiomarium.Core.Manifests;

namespace Axiomarium.Tests.Core;

public class YamlDocumentTests
{
    [Fact]
    public void Maps_scalars_with_the_core_schema()
    {
        var result = YamlDocument.Parse("a: 1\nb: 1.5\nc: true\nd: null\ne: ~\nf: yes\ng: \"1\"\nh: 0.1.0\ni: 0x1F\nj: |\n  7\n");

        Assert.Null(result.Problem);
        Assert.Equal(
            """{"a":1,"b":1.5,"c":true,"d":null,"e":null,"f":"yes","g":"1","h":"0.1.0","i":31,"j":"7\n"}""",
            result.Root!.ToJsonString());
    }

    [Fact]
    public void Numbers_keep_their_source_text()
    {
        var result = YamlDocument.Parse("a: 1.0\nb: 1.50\n");

        Assert.Equal("""{"a":1.0,"b":1.50}""", result.Root!.ToJsonString());
    }

    [Fact]
    public void Records_key_locations_for_nested_paths()
    {
        var result = YamlDocument.Parse("name: x\nsupports:\n  claude-code: full\ninputs:\n  - a\n");

        Assert.Null(result.Problem);
        Assert.Equal(new SourceLocation(1, 1), result.Locations["name"]);
        Assert.Equal(new SourceLocation(2, 1), result.Locations["supports"]);
        Assert.Equal(new SourceLocation(3, 3), result.Locations["supports.claude-code"]);
        Assert.Equal(new SourceLocation(5, 5), result.Locations["inputs[0]"]);
    }

    [Fact]
    public void Crlf_and_bom_parse_the_same()
    {
        const string text = "name: x\nsupports:\n  claude-code: full\ninputs:\n  - a\n";

        var lf = YamlDocument.Parse(text);
        var crlf = YamlDocument.Parse("﻿" + text.Replace("\n", "\r\n"));

        Assert.Null(crlf.Problem);
        Assert.Equal(lf.Root!.ToJsonString(), crlf.Root!.ToJsonString());
        Assert.Equal(lf.Locations.OrderBy(pair => pair.Key), crlf.Locations.OrderBy(pair => pair.Key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("# only a comment\n")]
    public void Empty_file_is_a_problem(string text)
    {
        var result = YamlDocument.Parse(text);

        Assert.Null(result.Root);
        Assert.Equal("The file is empty.", result.Problem!.Message);
    }

    [Fact]
    public void Two_documents_are_a_problem()
    {
        var result = YamlDocument.Parse("a: 1\n---\nb: 2\n");

        Assert.Null(result.Root);
        Assert.Equal("The file must hold one YAML document, found 2.", result.Problem!.Message);
        Assert.Equal(3, result.Problem.Location!.Value.Line);
    }

    [Fact]
    public void Syntax_error_has_a_location()
    {
        var result = YamlDocument.Parse("a: [1, 2\n");

        Assert.Null(result.Root);
        Assert.NotNull(result.Problem!.Location);
        Assert.DoesNotContain("Idx:", result.Problem.Message);
    }

    [Fact]
    public void Duplicate_key_is_a_problem_not_an_exception()
    {
        var result = YamlDocument.Parse("a: 1\na: 2\n");

        Assert.Null(result.Root);
        Assert.Equal("Duplicate key \"a\".", result.Problem!.Message);
        Assert.Equal(new SourceLocation(2, 1), result.Problem.Location);
    }

    [Fact]
    public void Tab_indentation_points_at_the_tab_line()
    {
        var result = YamlDocument.Parse(SampleManifests.Valid.Replace("  claude-code: experimental", "\tclaude-code: experimental"));

        Assert.Null(result.Root);
        Assert.Equal(7, result.Problem!.Location!.Value.Line);
    }

    [Fact]
    public void Unclosed_flow_mapping_is_a_problem_not_an_exception()
    {
        var result = YamlDocument.Parse(SampleManifests.Valid.Replace("kind: agent", "kind: {a: 1") + "}\n");

        Assert.Null(result.Root);
        Assert.NotNull(result.Problem);
    }

    [Theory]
    [InlineData("0xFFFFFFFFFFFFFFFFFFFF")]
    [InlineData("0o7777777777777777777777777")]
    [InlineData("01e999")]
    public void Out_of_range_numbers_stay_strings(string value)
    {
        var result = YamlDocument.Parse($"v: {value}\n");

        Assert.Null(result.Problem);
        Assert.Equal($$"""{"v":"{{value}}"}""", result.Root!.ToJsonString());
    }

    [Fact]
    public void Recursive_alias_is_a_problem_not_a_crash()
    {
        var result = YamlDocument.Parse("name: x\ninputs: &r [a, *r]\n");

        Assert.Null(result.Root);
        Assert.Equal("Recursive alias: a value can't contain itself.", result.Problem!.Message);
        Assert.Equal(2, result.Problem.Location!.Value.Line);
    }

    [Fact]
    public void Repeated_non_recursive_alias_is_fine()
    {
        var result = YamlDocument.Parse("a: &x [1, 2]\nb: *x\n");

        Assert.Null(result.Problem);
        Assert.Equal("""{"a":[1,2],"b":[1,2]}""", result.Root!.ToJsonString());
    }

    [Fact]
    public void Non_scalar_key_is_a_problem()
    {
        var result = YamlDocument.Parse("? [a, b]\n: 1\n");

        Assert.Null(result.Root);
        Assert.Equal("Mapping keys must be plain text.", result.Problem!.Message);
        Assert.Equal(1, result.Problem.Location!.Value.Line);
    }
}

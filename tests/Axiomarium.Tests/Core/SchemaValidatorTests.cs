using System.Text.Json.Nodes;
using Axiomarium.Core.Schemas;

namespace Axiomarium.Tests.Core;

public class SchemaValidatorTests
{
    private static IReadOnlyList<SchemaError> Validate(string instance, string schema) =>
        SchemaValidator.Validate(JsonNode.Parse(instance), JsonNode.Parse(schema)!.AsObject());

    [Fact]
    public void A_valid_instance_has_no_errors()
    {
        var errors = Validate(
            """{"name":"x","tags":["a"]}""",
            """{"type":"object","additionalProperties":false,"required":["name"],"properties":{"name":{"type":"string","minLength":1},"tags":{"type":"array","items":{"type":"string"}}}}""");

        Assert.Empty(errors);
    }

    [Fact]
    public void Enum_names_the_allowed_values()
    {
        var error = Assert.Single(Validate(
            """{"maturity":"production-ready"}""",
            """{"type":"object","properties":{"maturity":{"enum":["experimental","stable"]}}}"""));

        Assert.Equal("maturity", error.Path);
        Assert.Equal("Unknown maturity: \"production-ready\"", error.Message);
        Assert.Equal(["Allowed: experimental, stable"], error.Detail);
    }

    [Fact]
    public void Empty_value_for_an_enum_says_it_is_empty()
    {
        var error = Assert.Single(Validate(
            """{"maturity":null}""",
            """{"type":"object","properties":{"maturity":{"enum":["experimental","stable"]}}}"""));

        Assert.Equal("maturity is empty", error.Message);
        Assert.Equal(["Allowed: experimental, stable"], error.Detail);
    }

    [Fact]
    public void Missing_required_fields_are_listed_in_schema_order()
    {
        var errors = Validate(
            """{}""",
            """{"type":"object","required":["name","kind"],"properties":{"name":{"type":"string"},"kind":{"type":"string"}}}""");

        Assert.Equal(["Missing required field: name", "Missing required field: kind"], errors.Select(e => e.Message));
        Assert.All(errors, e => Assert.Equal("", e.Path));
    }

    [Fact]
    public void Unknown_field_is_reported_when_additional_properties_are_false()
    {
        var error = Assert.Single(Validate(
            """{"name":"x","maturty":"stable"}""",
            """{"type":"object","additionalProperties":false,"properties":{"name":{"type":"string"}}}"""));

        Assert.Equal("maturty", error.Path);
        Assert.Equal("Unknown field: maturty", error.Message);
    }

    [Fact]
    public void Wrong_type_names_both_types()
    {
        var error = Assert.Single(Validate(
            """{"name":true}""",
            """{"type":"object","properties":{"name":{"type":"string"}}}"""));

        Assert.Equal("name must be a string, found a boolean", error.Message);
        Assert.Empty(error.Detail);
    }

    [Fact]
    public void Number_where_a_string_is_required_says_to_quote_it()
    {
        var error = Assert.Single(Validate(
            """{"version":1.0}""",
            """{"type":"object","properties":{"version":{"type":"string"}}}"""));

        Assert.Equal("version must be a string, found a number", error.Message);
        Assert.Equal(["Quote it: \"1.0\""], error.Detail);
    }

    [Fact]
    public void Root_of_the_wrong_type_is_the_document()
    {
        var error = Assert.Single(Validate("null", """{"type":"object"}"""));

        Assert.Equal("", error.Path);
        Assert.Equal("The document must be a mapping, found null", error.Message);
    }

    [Fact]
    public void Pattern_mismatch_explains_the_format_with_the_description()
    {
        var error = Assert.Single(Validate(
            """{"name":"Determinism_Auditor"}""",
            """{"type":"object","properties":{"name":{"type":"string","pattern":"^[a-z-]+$","description":"Kebab-case: lowercase letters and hyphens."}}}"""));

        Assert.Equal("name \"Determinism_Auditor\" has the wrong format", error.Message);
        Assert.Equal(["Kebab-case: lowercase letters and hyphens."], error.Detail);
    }

    [Fact]
    public void Empty_string_breaks_min_length()
    {
        var error = Assert.Single(Validate(
            """{"description":""}""",
            """{"type":"object","properties":{"description":{"type":"string","minLength":1}}}"""));

        Assert.Equal("description must not be empty", error.Message);
    }

    [Fact]
    public void Min_properties_counts_entries()
    {
        var error = Assert.Single(Validate(
            """{"supports":{}}""",
            """{"type":"object","properties":{"supports":{"type":"object","minProperties":1}}}"""));

        Assert.Equal("supports needs at least 1 entry", error.Message);
    }

    [Fact]
    public void Ref_resolves_to_defs()
    {
        var error = Assert.Single(Validate(
            """{"supports":{"codex":"fullest"}}""",
            """{"type":"object","properties":{"supports":{"type":"object","properties":{"codex":{"$ref":"#/$defs/support"}}}},"$defs":{"support":{"enum":["full","partial"]}}}"""));

        Assert.Equal("supports.codex", error.Path);
        Assert.Equal("Unknown codex: \"fullest\"", error.Message);
        Assert.Equal(["Allowed: full, partial"], error.Detail);
    }

    [Fact]
    public void Items_are_validated_with_their_index_path()
    {
        var errors = Validate(
            """{"inputs":["",3]}""",
            """{"type":"object","properties":{"inputs":{"type":"array","items":{"type":"string","minLength":1}}}}""");

        Assert.Equal(["inputs[0]", "inputs[1]"], errors.Select(e => e.Path));
        Assert.Equal(["inputs[0] must not be empty", "inputs[1] must be a string, found a whole number"], errors.Select(e => e.Message));
    }

    [Fact]
    public void Nested_objects_report_nested_paths()
    {
        var errors = Validate(
            """{"permissions":{"shell":"none","disk":"read"}}""",
            """{"type":"object","properties":{"permissions":{"type":"object","additionalProperties":false,"required":["network"],"properties":{"shell":{"enum":["none"]},"network":{"enum":["none"]}}}}}""");

        Assert.Equal(["permissions", "permissions.disk"], errors.Select(e => e.Path));
        Assert.Equal(["Missing required field: permissions.network", "Unknown field: permissions.disk"], errors.Select(e => e.Message));
    }

    [Fact]
    public void Base_path_prefixes_every_path_and_message()
    {
        var errors = SchemaValidator.Validate(
            JsonNode.Parse("""{"event":"x","extra":1}"""),
            JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["command"],"properties":{"event":{"enum":["after-edit"]}}}""")!.AsObject(),
            "hook");

        Assert.Equal(["hook", "hook.event", "hook.extra"], errors.Select(e => e.Path));
        Assert.Equal(["Missing required field: hook.command", "Unknown event: \"x\"", "Unknown field: hook.extra"], errors.Select(e => e.Message));
    }
}

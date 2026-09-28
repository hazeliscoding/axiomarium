using Axiomarium.Core.Assets;
using Axiomarium.Core.Manifests;
using Axiomarium.Core.Schemas;

namespace Axiomarium.Tests.Core;

public class KindBlockSchemaTests
{
    private static IReadOnlyList<SchemaError> ValidateBlock(AssetKind kind, string yaml)
    {
        var parsed = YamlDocument.Parse(yaml);
        Assert.Null(parsed.Problem);
        return SchemaValidator.Validate(parsed.Root, SchemaCatalog.Block(kind)!, kind.ManifestName());
    }

    [Theory]
    [InlineData(AssetKind.Agent, false)]
    [InlineData(AssetKind.Skill, true)]
    [InlineData(AssetKind.Hook, true)]
    [InlineData(AssetKind.Policy, true)]
    [InlineData(AssetKind.Workflow, false)]
    [InlineData(AssetKind.Experiment, false)]
    public void Only_skills_hooks_and_policies_have_a_block(AssetKind kind, bool hasBlock)
    {
        Assert.Equal(hasBlock, SchemaCatalog.Block(kind) is not null);
    }

    [Fact]
    public void A_skill_block_is_valid_with_use_when()
    {
        Assert.Empty(ValidateBlock(AssetKind.Skill, "use_when: Writing or changing an asset.\n"));
    }

    [Fact]
    public void A_skill_block_without_use_when_is_invalid()
    {
        var errors = ValidateBlock(AssetKind.Skill, "when: Writing an asset.\n");

        Assert.Equal(["Missing required field: skill.use_when", "Unknown field: skill.when"], errors.Select(e => e.Message));
    }

    [Fact]
    public void A_hook_block_is_valid_with_an_event_a_command_and_a_response()
    {
        Assert.Empty(ValidateBlock(AssetKind.Hook, "event: after-edit\ncommand: axm hook scope-sheriff\nresponse: warn\n"));
    }

    [Fact]
    public void Unknown_hook_response_names_the_allowed_values()
    {
        var error = Assert.Single(ValidateBlock(AssetKind.Hook, "event: after-edit\ncommand: axm hook x\nresponse: shout\n"));

        Assert.Equal("hook.response", error.Path);
        Assert.Equal("Unknown response: \"shout\"", error.Message);
        Assert.Equal(["Allowed: block, warn, evidence"], error.Detail);
    }

    [Fact]
    public void Unknown_hook_event_names_the_allowed_values()
    {
        var error = Assert.Single(ValidateBlock(AssetKind.Hook, "event: on-save\ncommand: axm hook x\nresponse: warn\n"));

        Assert.Equal("Unknown event: \"on-save\"", error.Message);
        Assert.Equal(["Allowed: session-start, before-edit, after-edit"], error.Detail);
    }

    [Fact]
    public void A_policy_rule_names_what_enforces_it()
    {
        const string policy = """
            rules:
              - id: authorization-in-code
                rule: Authorization is decided by code, never by the model.
                enforced_by: [agents/determinism-auditor]
              - id: no-enforcement-yet
                rule: Retries are idempotent.
                enforced_by: []

            """;

        Assert.Empty(ValidateBlock(AssetKind.Policy, policy));
    }

    [Fact]
    public void A_policy_rule_must_say_what_enforces_it_even_when_nothing_does()
    {
        var error = Assert.Single(ValidateBlock(AssetKind.Policy, "rules:\n  - id: a\n    rule: A rule.\n"));

        Assert.Equal("policy.rules[0]", error.Path);
        Assert.Equal("Missing required field: policy.rules[0].enforced_by", error.Message);
    }

    [Fact]
    public void Enforced_by_must_be_an_asset_folder()
    {
        var error = Assert.Single(ValidateBlock(AssetKind.Policy, "rules:\n  - id: a\n    rule: A rule.\n    enforced_by: [determinism-auditor]\n"));

        Assert.Equal("policy.rules[0].enforced_by[0]", error.Path);
        Assert.Equal("policy.rules[0].enforced_by[0] \"determinism-auditor\" has the wrong format", error.Message);
    }

    [Fact]
    public void Rule_id_is_kebab_case()
    {
        var error = Assert.Single(ValidateBlock(AssetKind.Policy, "rules:\n  - id: Authorization_In_Code\n    rule: A rule.\n    enforced_by: []\n"));

        Assert.Equal("policy.rules[0].id", error.Path);
    }
}

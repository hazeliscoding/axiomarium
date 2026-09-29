using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Tests.Core;

public class TriggerExportTests
{
    private static readonly TriggerPromptFile Deploy = new(
        "deploy",
        new PromptSource("claude-code", "claude-opus-5-5", "2026-09-29"),
        [
            new TriggerPrompt("Ship \"v2\" to production.", PromptKind.Positive, true),
            new TriggerPrompt("Format this table.", PromptKind.Negative, false),
            new TriggerPrompt("Which one cuts the release?", PromptKind.Ambiguous, false, "tools:ship"),
        ]);

    [Fact]
    public void The_skill_creator_format_is_the_list_of_queries_and_whether_each_should_trigger()
    {
        Assert.Equal(
            """
            [
              {
                "query": "Ship \"v2\" to production.",
                "should_trigger": true
              },
              {
                "query": "Format this table.",
                "should_trigger": false
              },
              {
                "query": "Which one cuts the release?",
                "should_trigger": false
              }
            ]

            """,
            TriggerExport.SkillCreator(Deploy));
    }

    // An ambiguous prompt checks both sides: the skill it should pick, and the one it shouldn't.
    [Fact]
    public void The_promptfoo_format_asserts_skill_used_for_each_prompt_on_each_harness_the_skill_supports()
    {
        Assert.Equal(
            """
            # yaml-language-server: $schema=https://promptfoo.dev/config-schema.json
            # Trigger prompts for deploy, exported by axm triggers export. A model wrote them on 2026-09-29.
            # Save this at the repo root. skill-used passes only once deploy is installed where each harness finds skills.
            description: "Trigger prompts for deploy"
            prompts:
              - "{{request}}"
            providers:
              - id: anthropic:claude-agent-sdk
                config:
                  working_dir: .
                  setting_sources: ["user", "project"]
                  skills: all
              - id: openai:codex-sdk
                config:
                  working_dir: .
                  skip_git_repo_check: true
            tests:
              - description: "positive: Ship \"v2\" to production."
                vars:
                  request: "Ship \"v2\" to production."
                assert:
                  - type: skill-used
                    value: "deploy"
              - description: "negative: Format this table."
                vars:
                  request: "Format this table."
                assert:
                  - type: not-skill-used
                    value: "deploy"
              - description: "ambiguous: Which one cuts the release?"
                vars:
                  request: "Which one cuts the release?"
                assert:
                  - type: not-skill-used
                    value: "deploy"
                  - type: skill-used
                    value: "tools:ship"

            """,
            TriggerExport.Promptfoo(Deploy, [Harness.ClaudeCode, Harness.Codex]));
    }

    [Fact]
    public void A_skill_on_one_harness_gets_one_provider()
    {
        var config = TriggerExport.Promptfoo(Deploy with { Generated = null }, [Harness.Codex]);

        Assert.DoesNotContain("claude-agent-sdk", config);
        Assert.Contains("  - id: openai:codex-sdk\n", config);
        Assert.StartsWith("# yaml-language-server: $schema=https://promptfoo.dev/config-schema.json\n# Trigger prompts for deploy, exported by axm triggers export.\n", config);
    }
}

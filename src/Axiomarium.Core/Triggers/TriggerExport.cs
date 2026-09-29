using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Core.Triggers;

/// <summary>
/// Writes a skill's trigger prompts in the formats other skill-eval tools run, so <c>axm</c> emits fixtures for
/// them instead of competing with them.
/// </summary>
public static class TriggerExport
{
    /// <summary>
    /// The skill-creator's trigger eval set: a JSON list of <c>{"query", "should_trigger"}</c> objects, one per prompt,
    /// in file order.
    /// </summary>
    /// <param name="file">The prompts.</param>
    /// <returns>The JSON, indented, ending in a newline.</returns>
    public static string SkillCreator(TriggerPromptFile file)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NewLine = "\n" }))
        {
            json.WriteStartArray();
            foreach (var prompt in file.Prompts)
            {
                json.WriteStartObject();
                json.WriteString("query", prompt.Prompt);
                json.WriteBoolean("should_trigger", prompt.ShouldTrigger);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    /// <summary>
    /// A <c>promptfooconfig.yaml</c> with a provider for each of <paramref name="harnesses"/> and a test per prompt,
    /// asserting <c>skill-used</c> when the prompt should pick the skill and <c>not-skill-used</c> when it shouldn't.
    /// An ambiguous prompt also asserts the opposite for its rival.
    /// </summary>
    /// <param name="file">The prompts.</param>
    /// <param name="harnesses">The harnesses the skill supports: Claude Code runs through the Claude Agent SDK, Codex through the Codex SDK.</param>
    /// <returns>The YAML, ending in a newline. It says who wrote the prompts, and that the skill must be installed for skill-used to pass.</returns>
    public static string Promptfoo(TriggerPromptFile file, IReadOnlyList<Harness> harnesses)
    {
        var yaml = new StringBuilder();
        void Line(string text) => yaml.Append(text).Append('\n');

        Line("# yaml-language-server: $schema=https://promptfoo.dev/config-schema.json");
        Line($"# Trigger prompts for {file.Skill}, exported by axm triggers export.{(file.Generated is { } source ? $" A model wrote them on {source.Date}." : "")}");
        Line($"# Save this at the repo root. skill-used passes only once {file.Skill} is installed where each harness finds skills.");
        Line($"description: {TriggerPrompts.Quoted($"Trigger prompts for {file.Skill}")}");
        Line("prompts:");
        Line("  - \"{{request}}\"");
        Line("providers:");
        if (harnesses.Contains(Harness.ClaudeCode))
        {
            Line("  - id: anthropic:claude-agent-sdk");
            Line("    config:");
            Line("      working_dir: .");
            Line("      setting_sources: [\"user\", \"project\"]");
            Line("      skills: all");
        }

        if (harnesses.Contains(Harness.Codex))
        {
            Line("  - id: openai:codex-sdk");
            Line("    config:");
            Line("      working_dir: .");
            Line("      skip_git_repo_check: true");
        }

        Line("tests:");
        foreach (var prompt in file.Prompts)
        {
            Line($"  - description: {TriggerPrompts.Quoted($"{TriggerPrompts.Name(prompt.Kind)}: {prompt.Prompt}")}");
            Line("    vars:");
            Line($"      request: {TriggerPrompts.Quoted(prompt.Prompt)}");
            Line("    assert:");
            Line($"      - type: {(prompt.ShouldTrigger ? "skill-used" : "not-skill-used")}");
            Line($"        value: {TriggerPrompts.Quoted(file.Skill)}");
            if (prompt is { Kind: PromptKind.Ambiguous, Rival: { } rival })
            {
                Line($"      - type: {(prompt.ShouldTrigger ? "not-skill-used" : "skill-used")}");
                Line($"        value: {TriggerPrompts.Quoted(rival)}");
            }
        }

        return yaml.ToString();
    }
}

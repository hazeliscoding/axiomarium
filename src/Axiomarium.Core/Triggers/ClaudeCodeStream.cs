using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axiomarium.Core.Triggers;

/// <summary>One thing the model did in a Claude Code session: called a tool, or wrote text.</summary>
/// <param name="Tool">The tool it called, such as <c>Skill</c> or <c>Bash</c>, or <see langword="null"/> for text.</param>
/// <param name="Skill">For a <c>Skill</c> call, the skill it loaded. Otherwise <see langword="null"/>.</param>
/// <param name="Text">For text, what it wrote. Otherwise <see langword="null"/>.</param>
public sealed record ClaudeCodeStep(string? Tool, string? Skill, string? Text);

/// <summary>What a <c>claude -p --output-format stream-json</c> session reported.</summary>
/// <param name="Model">The model, from the <c>init</c> event, or <see langword="null"/> when there was none.</param>
/// <param name="Version">The Claude Code version, from the <c>init</c> event.</param>
/// <param name="Skills">The names of the skills the session listed, from the <c>init</c> event.</param>
/// <param name="Steps">Each tool call and piece of text the model produced, in order.</param>
/// <param name="Result">The final <c>result</c> event's text, or <see langword="null"/> when the session was stopped before it.</param>
/// <param name="IsError">Whether the <c>result</c> event reports an error.</param>
public sealed record ClaudeCodeSession(
    string? Model, string? Version, IReadOnlyList<string> Skills, IReadOnlyList<ClaudeCodeStep> Steps, string? Result, bool IsError);

/// <summary>
/// Reads the events <c>claude -p --output-format stream-json --verbose</c> prints, one JSON object a line, as
/// confirmed on Claude Code 2.1.284 (see the M4 spike in <c>ROADMAP.md</c>).
/// </summary>
public static class ClaudeCodeStream
{
    /// <summary>Reads a session from its lines.</summary>
    /// <param name="lines">The lines the harness printed. Lines that aren't JSON events, such as a warning, are skipped.</param>
    /// <returns>The session, with whatever the lines held. A stopped session has no result.</returns>
    public static ClaudeCodeSession Read(IEnumerable<string> lines)
    {
        string? model = null, version = null, result = null;
        var isError = false;
        var skills = new List<string>();
        var steps = new List<ClaudeCodeStep>();
        foreach (var line in lines)
        {
            if (Event(line) is not { } e)
            {
                continue;
            }

            switch (Text(e, "type"))
            {
                case "system" when Text(e, "subtype") == "init":
                    model = Text(e, "model");
                    version = Text(e, "claude_code_version");
                    skills.AddRange((e["skills"] as JsonArray ?? []).Select(skill => skill is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null).OfType<string>());
                    break;
                case "assistant":
                    steps.AddRange(Content(e).Select(Step).OfType<ClaudeCodeStep>());
                    break;
                case "result":
                    result = Text(e, "result");
                    isError = e["is_error"]?.GetValueKind() == JsonValueKind.True;
                    break;
            }
        }

        return new ClaudeCodeSession(model, version, skills, steps, result, isError);
    }

    /// <summary>The skills a session loaded before its first other action: its <c>Skill</c> calls before any other tool.</summary>
    /// <param name="session">The session.</param>
    /// <returns>The skills, in the order it loaded them. Text between them isn't an action, so it doesn't end them.</returns>
    public static IReadOnlyList<string> Loads(ClaudeCodeSession session) =>
        [.. session.Steps.TakeWhile(step => step.Tool is null or "Skill").Select(step => step.Skill).OfType<string>()];

    /// <summary>
    /// Whether <paramref name="line"/> ends the skills a session loads: a tool call other than <c>Skill</c>, or
    /// the session's result. The runner stops a trigger test's session there.
    /// </summary>
    /// <param name="line">One line the harness printed.</param>
    /// <returns><see langword="true"/> for the first action that isn't loading a skill, or the end of the session.</returns>
    public static bool EndsPick(string line) =>
        Event(line) is { } e && (Text(e, "type") == "result"
            || (Text(e, "type") == "assistant" && Content(e).Any(block => Text(block, "type") == "tool_use" && Text(block, "name") != "Skill")));

    private static ClaudeCodeStep? Step(JsonObject block) => Text(block, "type") switch
    {
        "tool_use" => new ClaudeCodeStep(Text(block, "name"), Text(block, "name") == "Skill" && block["input"] is JsonObject input ? Text(input, "skill") : null, null),
        "text" => new ClaudeCodeStep(null, null, Text(block, "text")),
        _ => null,
    };

    private static IEnumerable<JsonObject> Content(JsonObject e) => (e["message"]?["content"] as JsonArray ?? []).OfType<JsonObject>();

    private static JsonObject? Event(string line)
    {
        if (!line.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}

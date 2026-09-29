using System.Text.Json;

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
            if (JsonEvents.Parse(line) is not { } e)
            {
                continue;
            }

            switch (JsonEvents.Text(e, "type"))
            {
                case "system" when JsonEvents.Text(e, "subtype") == "init":
                    model = JsonEvents.Text(e, "model");
                    version = JsonEvents.Text(e, "claude_code_version");
                    skills.AddRange(JsonEvents.Items(JsonEvents.Child(e, "skills")).Where(skill => skill.ValueKind == JsonValueKind.String).Select(skill => skill.GetString()!));
                    break;
                case "assistant":
                    steps.AddRange(Content(e).Select(Step).OfType<ClaudeCodeStep>());
                    break;
                case "result":
                    result = JsonEvents.Text(e, "result");
                    isError = JsonEvents.Child(e, "is_error")?.ValueKind == JsonValueKind.True;
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
        JsonEvents.Parse(line) is { } e && (JsonEvents.Text(e, "type") == "result"
            || (JsonEvents.Text(e, "type") == "assistant" && Content(e).Any(block => JsonEvents.Text(block, "type") == "tool_use" && JsonEvents.Text(block, "name") != "Skill")));

    private static ClaudeCodeStep? Step(JsonElement block) => JsonEvents.Text(block, "type") switch
    {
        "tool_use" => new ClaudeCodeStep(
            JsonEvents.Text(block, "name"), JsonEvents.Text(block, "name") == "Skill" ? JsonEvents.Text(JsonEvents.Child(block, "input"), "skill") : null, null),
        "text" => new ClaudeCodeStep(null, null, JsonEvents.Text(block, "text")),
        _ => null,
    };

    private static IEnumerable<JsonElement> Content(JsonElement e) =>
        JsonEvents.Items(JsonEvents.Child(JsonEvents.Child(e, "message"), "content")).Where(block => block.ValueKind == JsonValueKind.Object);
}

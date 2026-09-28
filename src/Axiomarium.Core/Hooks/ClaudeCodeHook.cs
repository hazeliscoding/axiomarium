using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axiomarium.Core.Hooks;

/// <summary>An edit the agent made: the session's working directory and the file it changed.</summary>
/// <param name="Cwd">The session's working directory.</param>
/// <param name="FilePath">The edited file, usually absolute.</param>
internal sealed record EditEvent(string Cwd, string FilePath);

/// <summary>What Claude Code sends a command hook on stdin, and what the hook prints back.</summary>
/// <remarks>
/// Confirmed against Claude Code 2.1.283 on 2026-09-27 (see the hooks decisions in ROADMAP.md): a
/// PostToolUse hook's <c>hookSpecificOutput.additionalContext</c> reaches the model without blocking,
/// and the editing tools are Write, Edit and NotebookEdit. Confirmed against Claude Code 2.1.284 on
/// 2026-09-28, following the SessionStart section of the hooks docs: a SessionStart hook's
/// <c>additionalContext</c> reaches the model, and its <c>systemMessage</c> doesn't, because it is
/// shown to the user.
/// </remarks>
internal static class ClaudeCodeHook
{
    /// <summary>Reads a PostToolUse edit from the hook input.</summary>
    /// <param name="input">The JSON on stdin.</param>
    /// <param name="edit">The edit, or <see langword="null"/> for any other event or tool, which the hook ignores.</param>
    /// <param name="problem">Why the input can't be read, when it can't.</param>
    /// <returns>Whether the input could be read.</returns>
    public static bool TryReadEdit(string input, out EditEvent? edit, out string? problem)
    {
        edit = null;
        if (!TryReadPayload(input, out var payload, out var eventName, out problem))
        {
            return false;
        }

        if (eventName != "PostToolUse")
        {
            return true;
        }

        if (Text(payload, "tool_name") is not { } tool)
        {
            problem = "The hook input has no tool_name.";
            return false;
        }

        var pathField = tool switch
        {
            "Write" or "Edit" => "file_path",
            "NotebookEdit" => "notebook_path",
            _ => null,
        };
        if (pathField is null)
        {
            return true;
        }

        if (Text(payload, "cwd") is not { } cwd)
        {
            problem = "The hook input has no cwd.";
            return false;
        }

        if (payload["tool_input"] is not JsonObject toolInput || Text(toolInput, pathField) is not { } path)
        {
            problem = $"The {tool} input has no {pathField}.";
            return false;
        }

        edit = new EditEvent(cwd, path);
        return true;
    }

    /// <summary>Reads the session's working directory from a SessionStart hook input.</summary>
    /// <param name="input">The JSON on stdin.</param>
    /// <param name="cwd">The working directory, or <see langword="null"/> for any other event, which the hook ignores.</param>
    /// <param name="problem">Why the input can't be read, when it can't.</param>
    /// <returns>Whether the input could be read.</returns>
    public static bool TryReadSessionStart(string input, out string? cwd, out string? problem)
    {
        cwd = null;
        if (!TryReadPayload(input, out var payload, out var eventName, out problem))
        {
            return false;
        }

        if (eventName != "SessionStart")
        {
            return true;
        }

        cwd = Text(payload, "cwd");
        problem = cwd is null ? "The hook input has no cwd." : null;
        return cwd is not null;
    }

    /// <summary>
    /// The SessionStart output that shows <paramref name="userMessage"/> to the user and hands
    /// <paramref name="modelContext"/> to the model. The model never sees the user's message.
    /// </summary>
    public static string SessionNotice(string userMessage, string modelContext)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("systemMessage", userMessage);
            writer.WriteStartObject("hookSpecificOutput");
            writer.WriteString("hookEventName", "SessionStart");
            writer.WriteString("additionalContext", modelContext);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The output that hands <paramref name="message"/> to the model after a tool ran, without blocking.</summary>
    public static string AdditionalContext(string message)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("hookSpecificOutput");
            writer.WriteString("hookEventName", "PostToolUse");
            writer.WriteString("additionalContext", message);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // The input as a JSON object, and the event it's for.
    private static bool TryReadPayload(string input, out JsonObject payload, out string eventName, out string? problem)
    {
        payload = [];
        eventName = "";
        problem = null;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(input);
        }
        catch (JsonException)
        {
            problem = "The hook input isn't valid JSON.";
            return false;
        }

        if (node is not JsonObject parsed)
        {
            problem = "The hook input isn't a JSON object.";
            return false;
        }

        if (Text(parsed, "hook_event_name") is not { } name)
        {
            problem = "The hook input has no hook_event_name.";
            return false;
        }

        (payload, eventName) = (parsed, name);
        return true;
    }

    private static string? Text(JsonObject payload, string field) =>
        payload[field] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}

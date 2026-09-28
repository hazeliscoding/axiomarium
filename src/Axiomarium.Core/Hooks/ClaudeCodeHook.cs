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
/// Confirmed against Claude Code 2.1.283 on 2026-09-27 (see the hooks decision in ROADMAP.md): a
/// PostToolUse hook's <c>hookSpecificOutput.additionalContext</c> reaches the model without blocking,
/// and the editing tools are Write, Edit and NotebookEdit.
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

        if (node is not JsonObject payload)
        {
            problem = "The hook input isn't a JSON object.";
            return false;
        }

        if (Text(payload, "hook_event_name") is not { } eventName)
        {
            problem = "The hook input has no hook_event_name.";
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

    private static string? Text(JsonObject payload, string field) =>
        payload[field] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}

using System.Text;
using System.Text.Json.Nodes;

namespace Axiomarium.GroundTruth;

/// <summary>A file a harness loaded, by its path in the scenario, such as repo/CLAUDE.md.</summary>
internal sealed record LoadedFile(string File, int Bytes, string? Scope = null, string? Reason = null, bool Cut = false);

/// <summary>A recording that can't be trusted, such as one that picked up a file from outside the scenario.</summary>
internal sealed class GroundTruthException(string message) : Exception(message);

internal static class Markers
{
    // Every Markdown file in a scenario carries this line, so a recording can name the file it came from.
    public static string For(string file) => $"MARKER {file}";

    // The marker must end its line, so "MARKER repo/AGENTS.md" doesn't match "MARKER repo/AGENTS.md.bak".
    public static int IndexIn(string text, string file)
    {
        var marker = For(file);
        for (var at = text.IndexOf(marker, StringComparison.Ordinal); at >= 0; at = text.IndexOf(marker, at + 1, StringComparison.Ordinal))
        {
            var end = at + marker.Length;
            if (end == text.Length || text[end] is '\n' or '\r')
            {
                return at;
            }
        }

        return -1;
    }
}

/// <summary>Reads the AGENTS.md block out of what `codex debug prompt-input` prints.</summary>
internal static class CodexBlock
{
    private const string Heading = "# AGENTS.md instructions for";
    private const string Open = "<INSTRUCTIONS>\n";
    private const string Close = "\n</INSTRUCTIONS>";
    private const string AfterGlobal = "\n\n--- project-doc ---\n\n";
    private const string BetweenProjectFiles = "\n\n";

    /// <summary>The scenario files in the block, in order. Throws when the block holds anything else.</summary>
    /// <param name="promptInput">The JSON `codex debug prompt-input` printed.</param>
    /// <param name="scenarioFiles">Each Markdown file in the scenario, by its scenario path, with its content.</param>
    public static IReadOnlyList<LoadedFile> Parse(string promptInput, IReadOnlyDictionary<string, string> scenarioFiles)
    {
        if (Instructions(promptInput) is not { } block)
        {
            return [];
        }

        var found = scenarioFiles
            .Select(pair => (File: pair.Key, Content: pair.Value, At: Markers.IndexIn(block, pair.Key)))
            .Where(file => file.At >= 0)
            .OrderBy(file => file.At)
            .ToList();

        // Rebuild the block from the files, the way Codex joins them. Anything that doesn't line up is
        // text from outside the scenario, and the recording can't be trusted.
        var loaded = new List<LoadedFile>();
        var position = 0;
        for (var i = 0; i < found.Count; i++)
        {
            if (i > 0)
            {
                var separator = IsGlobal(found[i - 1].File) ? AfterGlobal : BetweenProjectFiles;
                if (!block.AsSpan(position).StartsWith(separator, StringComparison.Ordinal))
                {
                    throw Foreign(position);
                }

                position += separator.Length;
            }

            // Codex trims the global file but not project files.
            var text = IsGlobal(found[i].File) ? found[i].Content.Trim() : found[i].Content;
            var common = block.AsSpan(position).CommonPrefixLength(text);
            var cut = common < text.Length;
            if (cut && position + common != block.Length)
            {
                throw Foreign(position + common);
            }

            loaded.Add(new LoadedFile(found[i].File, Encoding.UTF8.GetByteCount(text[..common]), Cut: cut));
            position += common;
        }

        return position == block.Length ? loaded : throw Foreign(position);
    }

    private static bool IsGlobal(string file) => file.StartsWith("home/", StringComparison.Ordinal);

    private static GroundTruthException Foreign(int position) =>
        new($"Codex loaded text the scenario can't account for, at character {position} of its AGENTS.md block: text from outside the scenario, or a file cut before its marker line. Put each marker on its file's first line, and make sure CODEX_HOME points at the scenario's home.");

    private static string? Instructions(string promptInput)
    {
        var texts = (JsonNode.Parse(promptInput) as JsonArray ?? [])
            .SelectMany(message => message?["content"] as JsonArray ?? [])
            .Select(content => content?["text"]?.GetValue<string>())
            .OfType<string>();
        foreach (var text in texts)
        {
            if (!text.StartsWith(Heading, StringComparison.Ordinal))
            {
                continue;
            }

            var start = text.IndexOf(Open, StringComparison.Ordinal) + Open.Length;
            var end = text.LastIndexOf(Close, StringComparison.Ordinal);
            return text[start..end];
        }

        return null;
    }
}

/// <summary>Reads what Claude Code loaded from a session transcript and the InstructionsLoaded hook's log.</summary>
internal static class ClaudeTranscript
{
    private const string ContentsOf = "Contents of ";

    /// <summary>The files loaded at launch, in context order, and the files loaded when the target was read.</summary>
    /// <param name="transcript">The session transcript's lines.</param>
    /// <param name="hookLog">The InstructionsLoaded hook's input, one JSON object per line.</param>
    /// <param name="runDirectory">The folder the scenario was copied to, which holds repo/ and home/.</param>
    public static (IReadOnlyList<LoadedFile> Launch, IReadOnlyList<LoadedFile> Read) Parse(
        IEnumerable<string> transcript, IEnumerable<string> hookLog, string runDirectory)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var reasons = new Dictionary<string, string>(comparer);
        foreach (var entry in hookLog.Where(line => line.Length > 0).Select(line => JsonNode.Parse(line)!))
        {
            reasons.TryAdd(Path.GetFullPath(entry["file_path"]!.GetValue<string>()), entry["load_reason"]!.GetValue<string>());
        }

        var launch = new List<LoadedFile>();
        var read = new List<LoadedFile>();
        foreach (var line in transcript.Where(line => line.Length > 0))
        {
            if (JsonNode.Parse(line)?["attachment"] is not JsonObject attachment)
            {
                continue;
            }

            switch (attachment["type"]?.GetValue<string>())
            {
                // Only the first: compaction repeats it.
                case "instructions" when launch.Count == 0:
                    foreach (var file in attachment["files"]!.AsArray())
                    {
                        launch.Add(Entry(file!["path"]!, file["type"]!, file["content"]!, reasons, runDirectory));
                    }

                    break;
                case "nested_memory":
                    var content = attachment["content"]!;
                    read.Add(Entry(attachment["path"]!, content["type"]!, content["content"]!, reasons, runDirectory));
                    break;

                // The built-in AGENTS.md plugin adds a nested AGENTS.md after a Read, as hook context.
                case "hook_additional_context":
                    foreach (var text in (attachment["content"] as JsonArray ?? []).Select(item => item?.GetValue<string>()).OfType<string>())
                    {
                        var colon = text.IndexOf(":\n\n", StringComparison.Ordinal);
                        if (text.StartsWith(ContentsOf, StringComparison.Ordinal) && colon > 0)
                        {
                            read.Add(Entry(text[ContentsOf.Length..colon], "Project", text[(colon + 3)..], reasons, runDirectory));
                        }
                    }

                    break;
            }
        }

        return (launch, read);
    }

    private static LoadedFile Entry(JsonNode path, JsonNode scope, JsonNode content, Dictionary<string, string> reasons, string runDirectory) =>
        Entry(path.GetValue<string>(), scope.GetValue<string>(), content.GetValue<string>(), reasons, runDirectory);

    private static LoadedFile Entry(string path, string scope, string content, Dictionary<string, string> reasons, string runDirectory)
    {
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(runDirectory, full);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            // Only the path: the file may be someone's private instructions.
            throw new GroundTruthException(
                $"Claude Code loaded {full}, which is outside the scenario. Run the recorder from a folder outside your home folder.");
        }

        var file = relative.Replace(Path.DirectorySeparatorChar, '/');
        if (Markers.IndexIn(content, file) < 0)
        {
            throw new GroundTruthException($"Claude Code loaded {file}, but it doesn't carry its marker line \"{Markers.For(file)}\".");
        }

        // AGENTS.md never fires InstructionsLoaded. Every other file does, so a missing reason means the hook
        // lost an event, and the recording can't be trusted.
        var reason = reasons.GetValueOrDefault(full)
            ?? (Path.GetFileName(full) == "AGENTS.md"
                ? "agents-md"
                : throw new GroundTruthException($"The InstructionsLoaded hook didn't report {file}. Record the scenario again."));
        return new LoadedFile(file, Encoding.UTF8.GetByteCount(content), scope, reason);
    }
}

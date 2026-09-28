using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Axiomarium.GroundTruth;

/// <summary>A skill in a harness's listing: a scenario skill by its file, or a skill built into the harness by its name alone.</summary>
/// <param name="Name">The name the listing shows.</param>
/// <param name="File">Its scenario file, such as repo/.claude/skills/deploy/SKILL.md, or null for a built-in skill.</param>
/// <param name="Entry">How much of its description the model sees: whole, cut or name-only.</param>
/// <param name="Chars">The length of its entry, where the harness's budget counts entries.</param>
internal sealed record ListedSkill(string Name, string? File, string Entry, int? Chars = null);

internal static class SkillEntries
{
    public const string Whole = "whole";
    public const string Cut = "cut";
    public const string NameOnly = "name-only";

    // A scenario skill's description starts with "MARKER <file>", because a listing shows descriptions, not
    // bodies. A skill without a description is listed with its first body line, which is the same marker.
    public static string? MarkedFile(string description)
    {
        const string prefix = "MARKER ";
        if (!description.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = description[prefix.Length..];
        var end = rest.IndexOfAny([' ', '\n']);
        return end < 0 ? rest : rest[..end];
    }

    public static string Slashes(string path) => path.Replace('\\', '/');
}

/// <summary>Reads the skills Claude Code listed for the model out of a session transcript.</summary>
internal static class ClaudeSkillListing
{
    /// <summary>The skills listed at launch, in listing order, and those added after the agent read or edited the target.</summary>
    /// <param name="transcript">The session transcript's lines.</param>
    /// <param name="scenarioSkills">Each scenario skill's listed name with its file. Two skills can share a name.</param>
    public static (IReadOnlyList<ListedSkill> Launch, IReadOnlyList<ListedSkill> Read) Parse(
        IEnumerable<string> transcript, IReadOnlyCollection<KeyValuePair<string, string>> scenarioSkills)
    {
        var byName = scenarioSkills.ToLookup(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var files = scenarioSkills.Select(pair => pair.Value).ToHashSet(StringComparer.Ordinal);
        List<ListedSkill>? launch = null;
        var read = new List<ListedSkill>();
        foreach (var line in transcript.Where(line => line.Length > 0))
        {
            if (JsonNode.Parse(line)?["attachment"] is not JsonObject attachment || attachment["type"]?.GetValue<string>() != "skill_listing")
            {
                continue;
            }

            var names = attachment["names"]!.AsArray().Select(name => name!.GetValue<string>()).ToList();
            var listed = Entries(attachment["content"]!.GetValue<string>(), names).Select(entry => Classify(entry.Name, entry.Text, byName, files));

            // Only the first full listing: a resumed session sends it again.
            if (attachment["isInitial"]?.GetValue<bool>() == true)
            {
                launch ??= [.. listed];
            }
            else
            {
                read.AddRange(listed);
            }
        }

        return (launch ?? [], read);
    }

    // An entry starts with "- name" and runs to the next entry, since a description can take several lines.
    private static List<(string Name, string Text)> Entries(string content, IReadOnlyList<string> names)
    {
        var entries = new List<(string Name, List<string> Lines)>();
        foreach (var line in content.Split('\n'))
        {
            var next = entries.Count < names.Count ? names[entries.Count] : null;
            if (next is not null && (line == $"- {next}" || line.StartsWith($"- {next}: ", StringComparison.Ordinal)))
            {
                entries.Add((next, [line]));
            }
            else if (entries.Count > 0)
            {
                entries[^1].Lines.Add(line);
            }
            else
            {
                throw new GroundTruthException("Claude Code's skill listing starts with something other than a skill entry.");
            }
        }

        if (entries.Count < names.Count)
        {
            throw new GroundTruthException($"Claude Code's skill listing names {names[entries.Count]} but has no entry for it.");
        }

        return [.. entries.Select(entry => (entry.Name, string.Join('\n', entry.Lines)))];
    }

    private static ListedSkill Classify(string name, string text, ILookup<string, string> byName, HashSet<string> files)
    {
        var candidates = byName[name].ToList();
        if (text == $"- {name}")
        {
            return candidates.Count <= 1
                ? new ListedSkill(name, candidates.SingleOrDefault(), SkillEntries.NameOnly, text.Length)
                : throw new GroundTruthException($"Claude Code listed {name} without its description, and the scenario has more than one skill by that name, so the recording can't tell which. Rename one, or keep the listing under its budget.");
        }

        var description = text[(name.Length + 4)..];
        var entry = description.EndsWith('…') ? SkillEntries.Cut : SkillEntries.Whole;
        if (SkillEntries.MarkedFile(description) is not { } file)
        {
            // Anything else unmarked would be a skill from outside the scenario; the recorder rules those out
            // before the session starts, so what's left is built into Claude Code.
            return candidates.Count == 0
                ? new ListedSkill(name, null, entry, text.Length)
                : throw new GroundTruthException($"Claude Code listed the scenario's {name} skill without its marker. Start its description with \"MARKER {candidates[0]}\".");
        }

        return files.Contains(file)
            ? new ListedSkill(name, file, entry, text.Length)
            : throw new GroundTruthException($"Claude Code listed a skill marked {file}, which isn't a skill in the scenario.");
    }
}

/// <summary>Reads the skills Codex listed for the model out of what `codex debug prompt-input` prints.</summary>
internal static partial class CodexSkillBlock
{
    private const string FileOpen = " (file: ";

    /// <summary>The listed skills, in listing order. Throws when one comes from outside the scenario.</summary>
    /// <param name="promptInput">The JSON `codex debug prompt-input` printed.</param>
    /// <param name="runDirectory">The folder the scenario was copied to, which holds repo/ and home/.</param>
    /// <param name="scenarioSkills">Each SKILL.md in the scenario, by its scenario path.</param>
    public static IReadOnlyList<ListedSkill> Parse(string promptInput, string runDirectory, IReadOnlySet<string> scenarioSkills)
    {
        var texts = (JsonNode.Parse(promptInput) as JsonArray ?? [])
            .SelectMany(message => message?["content"] as JsonArray ?? [])
            .Select(content => content?["text"]?.GetValue<string>())
            .OfType<string>();
        if (texts.FirstOrDefault(text => text.Contains("<skills_instructions>", StringComparison.Ordinal)) is not { } block)
        {
            return [];
        }

        var run = SkillEntries.Slashes(runDirectory).TrimEnd('/') + "/";
        var roots = new Dictionary<string, string>(StringComparer.Ordinal);
        var listed = new List<ListedSkill>();
        var section = "";
        foreach (var line in block.Split('\n'))
        {
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                section = line;
            }
            else if (section == "### Skill roots" && Root().Match(line) is { Success: true } root)
            {
                roots[root.Groups[1].Value] = root.Groups[2].Value;
            }
            else if (section == "### Available skills" && line.StartsWith("- ", StringComparison.Ordinal))
            {
                listed.Add(Entry(line, roots, run, scenarioSkills));
            }
        }

        return listed;
    }

    private static ListedSkill Entry(string line, Dictionary<string, string> roots, string run, IReadOnlySet<string> scenarioSkills)
    {
        var colon = line.IndexOf(": ", StringComparison.Ordinal);
        var open = line.LastIndexOf(FileOpen, StringComparison.Ordinal);
        if (colon < 0 || open < colon || !line.EndsWith(')'))
        {
            throw new GroundTruthException($"Codex listed a skill in a form the recorder doesn't know: {line[..Math.Min(line.Length, 40)]}");
        }

        var name = line[2..colon];
        var description = line[(colon + 2)..open];
        var path = line[(open + FileOpen.Length)..^1];
        var slash = path.IndexOf('/');
        if (slash > 0 && roots.TryGetValue(path[..slash], out var root))
        {
            path = root + path[slash..];
        }

        path = SkillEntries.Slashes(path);
        var entry = description.EndsWith("...", StringComparison.Ordinal) ? SkillEntries.Cut : SkillEntries.Whole;
        if (path.StartsWith(run + "home/.codex/skills/.system/", StringComparison.Ordinal))
        {
            return new ListedSkill(name, null, entry);
        }

        // Only the path: the skill may be someone's private one.
        var file = path.StartsWith(run, StringComparison.Ordinal) ? path[run.Length..] : null;
        if (file is null || !scenarioSkills.Contains(file))
        {
            throw new GroundTruthException($"Codex listed the skill in {path}, which isn't a skill in the scenario. Make sure HOME and CODEX_HOME point at the scenario's home.");
        }

        return SkillEntries.MarkedFile(description) == file
            ? new ListedSkill(name, file, entry)
            : throw new GroundTruthException($"Codex listed {file} without its marker. Start its description with \"MARKER {file}\".");
    }

    [GeneratedRegex(@"^- `(r\d+)` = `(.*)`$")]
    private static partial Regex Root();
}

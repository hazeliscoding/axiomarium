using System.Text.Json.Nodes;
using Axiomarium.Core.Manifests;

namespace Axiomarium.GroundTruth;

/// <summary>A scenario: a tiny repo and a fake home, where the harness launches, and the file the agent reads or edits.</summary>
/// <param name="Name">Its folder name under scenarios/.</param>
/// <param name="Directory">Its folder, holding scenario.yaml, repo/, home/ and expected.json.</param>
/// <param name="Description">What it shows, in a sentence.</param>
/// <param name="Launch">The launch directory, relative to repo/.</param>
/// <param name="Target">The file the agent reads, relative to repo/.</param>
/// <param name="Action">What the agent does with the target: read it, or read it and then edit it.</param>
internal sealed record Scenario(string Name, string Directory, string Description, string Launch, string Target, string Action = Scenario.Read)
{
    public const string RecordingFile = "expected.json";
    public const string Read = "read";
    public const string Edit = "edit";

    private static readonly string[] HookFiles = ["settings.json", "settings.local.json", "hooks.json"];

    public static Scenario Load(string directory)
    {
        var name = Path.GetFileName(directory);
        var parsed = YamlDocument.Parse(File.ReadAllText(Path.Combine(directory, "scenario.yaml")));
        if (parsed.Problem is { } problem)
        {
            throw new GroundTruthException($"{name}/scenario.yaml: {problem.Message}");
        }

        string? Optional(string field) => parsed.Root?[field]?.GetValue<string>();
        string Field(string field) => Optional(field) ?? throw new GroundTruthException($"{name}/scenario.yaml has no {field}.");

        var action = Optional("action") ?? Read;
        if (action is not (Read or Edit))
        {
            throw new GroundTruthException($"{name}/scenario.yaml: action is {action}, but it can only be {Read} or {Edit}.");
        }

        return new Scenario(name, directory, Field("description"), Field("launch"), Field("target"), action);
    }

    /// <summary>Every Markdown file under repo/ and home/, by its scenario path such as repo/CLAUDE.md, with its content.</summary>
    public IReadOnlyDictionary<string, string> MarkdownFiles()
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in new[] { "repo", "home" }.Select(part => Path.Combine(Directory, part)).Where(System.IO.Directory.Exists))
        {
            foreach (var path in System.IO.Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
            {
                files[Path.GetRelativePath(Directory, path).Replace(Path.DirectorySeparatorChar, '/')] = File.ReadAllText(path);
            }
        }

        return files;
    }

    /// <summary>Every SKILL.md in the scenario, by its scenario path.</summary>
    public IReadOnlySet<string> SkillFiles() =>
        MarkdownFiles().Keys.Where(file => file.EndsWith("/SKILL.md", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Each skill Claude Code could list from the scenario, by the name it lists it under: a skill's folder name,
    /// or a command's path under its commands folder, with a colon for each subfolder.
    /// </summary>
    public IReadOnlyCollection<KeyValuePair<string, string>> ClaudeSkillNames()
    {
        var names = new List<KeyValuePair<string, string>>();
        foreach (var file in MarkdownFiles().Keys)
        {
            var parts = file.Split('/');
            var skills = Array.IndexOf(parts, "skills");
            var commands = Array.IndexOf(parts, "commands");
            if (file.EndsWith("/SKILL.md", StringComparison.Ordinal) && skills > 0 && parts[skills - 1] == ".claude" && skills == parts.Length - 3)
            {
                names.Add(new(parts[skills + 1], file));
            }
            else if (commands > 0 && parts[commands - 1] == ".claude")
            {
                names.Add(new(string.Join(':', parts[(commands + 1)..])[..^".md".Length], file));
            }
        }

        return names;
    }

    /// <summary>Whether a scenario file is a skill or a Claude Code command, whose description a listing shows.</summary>
    public static bool IsSkill(string file) =>
        file.EndsWith("/SKILL.md", StringComparison.Ordinal)
        || (file.Contains("/.claude/commands/", StringComparison.Ordinal) && file.EndsWith(".md", StringComparison.Ordinal));

    /// <summary>The hook commands in each settings or hooks file, by the file's scenario path.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> HookCommands()
    {
        var files = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json", SearchOption.AllDirectories).Where(path => HookFiles.Contains(Path.GetFileName(path))))
        {
            var commands = new List<string>();
            Commands(JsonNode.Parse(File.ReadAllText(path))?["hooks"], commands);
            if (commands.Count > 0)
            {
                files[Path.GetRelativePath(Directory, path).Replace(Path.DirectorySeparatorChar, '/')] = commands;
            }
        }

        return files;
    }

    private static void Commands(JsonNode? node, List<string> commands)
    {
        switch (node)
        {
            case JsonObject item:
                foreach (var (key, value) in item)
                {
                    if (key == "command" && value is JsonValue command && command.TryGetValue<string>(out var text))
                    {
                        commands.Add(text);
                    }
                    else
                    {
                        Commands(value, commands);
                    }
                }

                break;
            case JsonArray items:
                foreach (var value in items)
                {
                    Commands(value, commands);
                }

                break;
        }
    }

    /// <summary>A top-level field of a Markdown file's YAML frontmatter, without quotes, or null when it has none.</summary>
    public static string? FrontmatterValue(string content, string field)
    {
        var end = content.StartsWith("---\n", StringComparison.Ordinal) ? content.IndexOf("\n---\n", 3, StringComparison.Ordinal) : -1;
        if (end < 4)
        {
            return null;
        }

        var line = content[4..end].Split('\n').FirstOrDefault(line => line.StartsWith(field + ":", StringComparison.Ordinal));
        return line?[(field.Length + 1)..].Trim().Trim('"', '\'');
    }

    /// <summary>The first line after any YAML frontmatter, which is where a file's marker goes.</summary>
    public static string FirstBodyLine(string content)
    {
        var body = content;
        if (content.StartsWith("---\n", StringComparison.Ordinal))
        {
            var end = content.IndexOf("\n---\n", 4, StringComparison.Ordinal);
            body = end < 0 ? content : content[(end + 5)..];
        }

        var newline = body.IndexOf('\n');
        return newline < 0 ? body : body[..newline];
    }
}

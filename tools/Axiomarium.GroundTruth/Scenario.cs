using Axiomarium.Core.Manifests;

namespace Axiomarium.GroundTruth;

/// <summary>A scenario: a tiny repo and a fake home, where the harness launches, and the file the agent reads.</summary>
/// <param name="Name">Its folder name under scenarios/.</param>
/// <param name="Directory">Its folder, holding scenario.yaml, repo/, home/ and expected.json.</param>
/// <param name="Description">What it shows, in a sentence.</param>
/// <param name="Launch">The launch directory, relative to repo/.</param>
/// <param name="Target">The file the agent reads, relative to repo/.</param>
internal sealed record Scenario(string Name, string Directory, string Description, string Launch, string Target)
{
    public const string RecordingFile = "expected.json";

    public static Scenario Load(string directory)
    {
        var name = Path.GetFileName(directory);
        var parsed = YamlDocument.Parse(File.ReadAllText(Path.Combine(directory, "scenario.yaml")));
        if (parsed.Problem is { } problem)
        {
            throw new GroundTruthException($"{name}/scenario.yaml: {problem.Message}");
        }

        string Field(string field) =>
            parsed.Root?[field]?.GetValue<string>() ?? throw new GroundTruthException($"{name}/scenario.yaml has no {field}.");

        return new Scenario(name, directory, Field("description"), Field("launch"), Field("target"));
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

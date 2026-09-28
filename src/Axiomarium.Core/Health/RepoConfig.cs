using System.Text.Json.Nodes;
using Axiomarium.Core.Manifests;
using Axiomarium.Core.Paths;
using Axiomarium.Core.Schemas;

namespace Axiomarium.Core.Health;

/// <summary>What a repo's <c>axiomarium.yaml</c> says about how <c>axm</c> treats it.</summary>
/// <param name="DoctorIgnore">Globs, relative to the repo root, for instruction files the doctor leaves out.</param>
public sealed record RepoConfig(IReadOnlyList<Glob> DoctorIgnore)
{
    /// <summary>The file's name, at the repo root.</summary>
    public const string FileName = "axiomarium.yaml";

    /// <summary>The configuration of a repo with no <c>axiomarium.yaml</c>: nothing ignored.</summary>
    public static RepoConfig Empty { get; } = new([]);

    /// <summary>Reads <c>axiomarium.yaml</c> at <paramref name="repoRoot"/> and checks it against <c>schemas/axiomarium.schema.json</c>.</summary>
    /// <param name="repoRoot">The repo's root folder.</param>
    /// <returns>
    /// The configuration, and an error for each problem in the file, at its line. A file with problems
    /// configures nothing, so a typo never silently hides a finding. No file is no problem.
    /// </returns>
    public static (RepoConfig Config, IReadOnlyList<Diagnostic> Diagnostics) Load(string repoRoot)
    {
        var path = Path.Combine(repoRoot, FileName);
        if (!File.Exists(path))
        {
            return (Empty, []);
        }

        var parsed = YamlDocument.Parse(File.ReadAllText(path));
        if (parsed.Problem is { } problem)
        {
            return (Empty, [new Diagnostic(Severity.Error, FileName, problem.Location, problem.Message, [])]);
        }

        var diagnostics = SchemaValidator.Validate(parsed.Root, SchemaCatalog.Repo)
            .Select(error => new Diagnostic(Severity.Error, FileName, Doctor.Locate(parsed.Locations, error.Path), error.Message, error.Detail))
            .ToList();
        if (diagnostics.Count > 0)
        {
            return (Empty, diagnostics);
        }

        var ignore = new List<Glob>();
        var patterns = (parsed.Root as JsonObject)?["doctor"]?["ignore"] as JsonArray ?? [];
        for (var i = 0; i < patterns.Count; i++)
        {
            var field = $"doctor.ignore[{i}]";
            if (Glob.TryParse(patterns[i]!.GetValue<string>(), out var glob, out var invalid))
            {
                ignore.Add(glob);
            }
            else
            {
                diagnostics.Add(new Diagnostic(Severity.Error, FileName, Doctor.Locate(parsed.Locations, field), $"{field} isn't a valid pattern: {invalid}", []));
            }
        }

        return diagnostics.Count > 0 ? (Empty, diagnostics) : (new RepoConfig(ignore), []);
    }
}

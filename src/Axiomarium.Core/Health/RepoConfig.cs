using System.Text.Json.Nodes;
using Axiomarium.Core.Evidence;
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

    /// <summary>The checks <c>evidence.checks</c> declares, in order, or none.</summary>
    public IReadOnlyList<EvidenceCheck> EvidenceChecks { get; init; } = [];

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

        void Problem(string field, string message) =>
            diagnostics.Add(new Diagnostic(Severity.Error, FileName, Doctor.Locate(parsed.Locations, field), message, []));
        List<Glob> Globs(JsonArray? patterns, string field)
        {
            var globs = new List<Glob>();
            for (var i = 0; i < (patterns?.Count ?? 0); i++)
            {
                if (Glob.TryParse(patterns![i]!.GetValue<string>(), out var glob, out var invalid))
                {
                    globs.Add(glob);
                }
                else
                {
                    Problem($"{field}[{i}]", $"{field}[{i}] isn't a valid pattern: {invalid}");
                }
            }

            return globs;
        }

        var ignore = Globs((parsed.Root as JsonObject)?["doctor"]?["ignore"] as JsonArray, "doctor.ignore");
        var checks = new List<EvidenceCheck>();
        var declared = (parsed.Root as JsonObject)?["evidence"]?["checks"] as JsonArray ?? [];
        for (var i = 0; i < declared.Count; i++)
        {
            var entry = declared[i]!.AsObject();
            var name = entry["name"]!.GetValue<string>();
            if (checks.Any(check => check.Name == name))
            {
                Problem($"evidence.checks[{i}].name", $"evidence.checks[{i}].name: {name} is declared twice.");
            }

            var run = entry["run"]!.AsArray().Select(command => command!.GetValue<string>()).ToList();
            checks.Add(new EvidenceCheck(name, run, Globs(entry["covers"] as JsonArray, $"evidence.checks[{i}].covers")));
        }

        return diagnostics.Count > 0 ? (Empty, diagnostics) : (new RepoConfig(ignore) { EvidenceChecks = checks }, []);
    }
}

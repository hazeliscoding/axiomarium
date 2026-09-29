using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axiomarium.Core.Assets;
using Axiomarium.Core.Evals;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Manifests;
using Axiomarium.Core.Registry;
using Axiomarium.Core.Schemas;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Core.Health;

/// <summary>
/// Checks the health of a vault: finds every asset, validates its manifest and content file, and checks
/// that what a valid manifest points to exists and that its maturity has its evidence. Reads, never writes.
/// </summary>
public static partial class Doctor
{
    private const string ManifestName = "asset.yaml";

    /// <summary>
    /// Examines any repo: its instruction files, launched from the repo root, always, and the vault checks
    /// when the folder or the repo root is a vault. Reads <c>axiomarium.yaml</c> for what to leave out.
    /// </summary>
    /// <param name="folder">Where to start: the repo root is the nearest folder at or above it with a <c>.git</c>, or the folder itself.</param>
    /// <param name="machine">Where the harnesses' user and managed files are.</param>
    /// <returns>A report, or the reason the doctor couldn't run: nothing exists at the path, or the path is a file.</returns>
    public static HealthResult Examine(string folder, Machine machine)
    {
        var start = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (File.Exists(start))
        {
            return new HealthResult(null, new VaultProblem(VaultProblemKind.NotAFolder, $"{start} is a file, not a folder."));
        }

        if (!Directory.Exists(start))
        {
            return new HealthResult(null, new VaultProblem(VaultProblemKind.FolderMissing, $"The folder {start} does not exist."));
        }

        var repoRoot = Instructions.Paths.Upward(start, machine.FileSystemRoot).FirstOrDefault(directory => Path.Exists(Path.Combine(directory, ".git"))) ?? start;
        var vault = new[] { start, repoRoot }.Select(candidate => Run(candidate).Report).FirstOrDefault(report => report is not null);
        var (config, problems) = RepoConfig.Load(repoRoot);
        var instructions = InstructionFindings.Check(repoRoot, machine, config.DoctorIgnore);
        return new HealthResult(new HealthReport(repoRoot, vault, problems, instructions), null);
    }

    /// <summary>Examines the vault at <paramref name="vaultRoot"/>.</summary>
    /// <param name="vaultRoot">The directory that holds <c>agents/</c>, <c>skills/</c> and the other kind folders.</param>
    /// <returns>
    /// A report, or the reason the doctor couldn't run: nothing exists at the path, the path is a file,
    /// or none of the kind folders exist. An empty kind folder is a vault with no assets, not a reason to stop.
    /// </returns>
    public static DoctorResult Run(string vaultRoot)
    {
        if (File.Exists(vaultRoot))
        {
            return new DoctorResult(null, new VaultProblem(VaultProblemKind.NotAFolder, $"{vaultRoot} is a file, not a folder."));
        }

        if (!Directory.Exists(vaultRoot))
        {
            return new DoctorResult(null, new VaultProblem(VaultProblemKind.FolderMissing, $"The folder {vaultRoot} does not exist."));
        }

        var kinds = AssetKinds.All.Where(kind => Directory.Exists(Path.Combine(vaultRoot, kind.Folder()))).ToList();
        if (kinds.Count == 0)
        {
            var folders = string.Join(", ", AssetKinds.All.Select(kind => kind.Folder() + "/"));
            return new DoctorResult(null, new VaultProblem(VaultProblemKind.NotAVault, $"No vault found in {vaultRoot}. A vault has at least one of: {folders}."));
        }

        var assets = new List<DiscoveredAsset>();
        var found = new Findings();
        foreach (var kind in kinds)
        {
            var names = Directory.EnumerateDirectories(Path.Combine(vaultRoot, kind.Folder()))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => !name.StartsWith('.'))
                .Order(StringComparer.Ordinal);
            foreach (var name in names)
            {
                assets.Add(Examine(vaultRoot, kind, name, found));
            }
        }

        References.ExamineTargets(assets, found.References, found.Diagnostics);
        MaturityCheck.Examine(found.Claims, ReadUsage(vaultRoot, found.Diagnostics), found.Diagnostics);

        var ordered = found.Diagnostics
            .OrderBy(diagnostic => diagnostic.File, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location?.Line ?? 0)
            .ToList();
        return new DoctorResult(new DoctorReport(assets, ordered), null);
    }

    private static DiscoveredAsset Examine(string vaultRoot, AssetKind kind, string name, Findings found)
    {
        var diagnostics = found.Diagnostics;
        var folder = $"{kind.Folder()}/{name}";
        var directory = Path.Combine(vaultRoot, kind.Folder(), name);

        // Names are compared exactly, because Windows and macOS would find agent.md as Agent.md and
        // Linux wouldn't, and the vault has to mean the same thing on every platform.
        var files = Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>().ToList();
        ExamineContent(kind, folder, directory, files, diagnostics);
        if (kind == AssetKind.Skill)
        {
            ExamineTriggerPrompts(name, folder, directory, diagnostics);
        }

        ExamineEvalCases(folder, directory, diagnostics);

        if (!files.Contains(ManifestName, StringComparer.Ordinal))
        {
            diagnostics.Add(new Diagnostic(Severity.Error, folder, null, $"Missing {ManifestName}", RenameHint(files, ManifestName, "asset.yml") ?? []));
            return new DiscoveredAsset(kind, name, folder, null, null);
        }

        var manifestFile = $"{folder}/{ManifestName}";

        // One unreadable or surprising manifest becomes a diagnostic on that file, so the rest of
        // the vault is still checked and the user learns which file is at fault.
        try
        {
            var text = File.ReadAllText(Path.Combine(directory, ManifestName));
            return ExamineManifest(kind, name, folder, directory, manifestFile, text, found);
        }
        catch (Exception problem) when (problem is not OutOfMemoryException)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, manifestFile, null, $"Couldn't read {ManifestName}: {problem.Message}", []));
            return new DiscoveredAsset(kind, name, folder, manifestFile, null);
        }
    }

    private static void ExamineContent(AssetKind kind, string folder, string directory, List<string> files, List<Diagnostic> diagnostics)
    {
        var content = kind.ContentFile();
        if (!files.Contains(content, StringComparer.Ordinal))
        {
            var detail = RenameHint(files, content) ?? ["An asset keeps its content in a Markdown file named after its kind."];
            diagnostics.Add(new Diagnostic(Severity.Error, folder, null, $"Missing {content}", detail));
            return;
        }

        var contentFile = $"{folder}/{content}";
        try
        {
            if (string.IsNullOrWhiteSpace(File.ReadAllText(Path.Combine(directory, content))))
            {
                diagnostics.Add(new Diagnostic(Severity.Error, contentFile, null, $"{content} is empty", ["Write the asset's content in it."]));
            }
        }
        catch (Exception problem) when (problem is not OutOfMemoryException)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, contentFile, null, $"Couldn't read {content}: {problem.Message}", []));
        }
    }

    // A skill's trigger prompts are its trigger evals, so a broken file is an error like a broken manifest.
    private static void ExamineTriggerPrompts(string name, string folder, string directory, List<Diagnostic> diagnostics)
    {
        var path = Path.Combine(directory, TriggerPrompts.RelativePath);
        if (!File.Exists(path))
        {
            return;
        }

        var file = $"{folder}/{TriggerPrompts.RelativePath}";
        try
        {
            foreach (var problem in TriggerPrompts.Read(File.ReadAllText(path), name).Problems)
            {
                diagnostics.Add(new Diagnostic(Severity.Error, file, problem.Location, problem.Message, problem.Detail));
            }
        }
        catch (Exception problem) when (problem is not OutOfMemoryException)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, file, null, $"Couldn't read {TriggerPrompts.RelativePath}: {problem.Message}", []));
        }
    }

    // An asset's eval cases are its behavioral and regression evals, so a broken case is an error like a broken manifest.
    private static void ExamineEvalCases(string folder, string directory, List<Diagnostic> diagnostics)
    {
        foreach (var type in Enum.GetValues<EvalType>())
        {
            var evals = $"{folder}/evals/{EvalCases.Folder(type)}";
            var path = Path.Combine(directory, "evals", EvalCases.Folder(type));
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var loose in Visible(Directory.EnumerateFiles(path)))
            {
                diagnostics.Add(new Diagnostic(
                    Severity.Error, $"{evals}/{loose}", null, $"{loose} isn't in a case folder", ["Each case is a folder with its eval.yaml and, when the session needs files, a repo/."]));
            }

            foreach (var name in Visible(Directory.EnumerateDirectories(path)))
            {
                var caseFolder = $"{evals}/{name}";
                if (!KebabCase().IsMatch(name))
                {
                    diagnostics.Add(new Diagnostic(
                        Severity.Error, caseFolder, null, $"The case folder {name} isn't kebab-case", ["Name it in lowercase words joined by hyphens, such as new-hook."]));
                    continue;
                }

                var files = Directory.EnumerateFiles(Path.Combine(path, name)).Select(Path.GetFileName).OfType<string>().ToList();
                if (!files.Contains(EvalCases.FileName, StringComparer.Ordinal))
                {
                    diagnostics.Add(new Diagnostic(
                        Severity.Error, caseFolder, null, $"Missing {EvalCases.FileName}", RenameHint(files, EvalCases.FileName, "eval.yml") ?? ["A case says what to ask and what to check in eval.yaml."]));
                    continue;
                }

                var file = $"{caseFolder}/{EvalCases.FileName}";
                try
                {
                    foreach (var problem in EvalCases.Read(File.ReadAllText(Path.Combine(path, name, EvalCases.FileName)), name, type).Problems)
                    {
                        diagnostics.Add(new Diagnostic(Severity.Error, file, problem.Location, problem.Message, problem.Detail));
                    }
                }
                catch (Exception problem) when (problem is not OutOfMemoryException)
                {
                    diagnostics.Add(new Diagnostic(Severity.Error, file, null, $"Couldn't read {EvalCases.FileName}: {problem.Message}", []));
                }
            }
        }
    }

    // Hidden files and folders, such as .gitkeep, only keep a folder in git.
    private static IEnumerable<string> Visible(IEnumerable<string> paths) =>
        paths.Select(Path.GetFileName).OfType<string>().Where(name => !name.StartsWith('.')).Order(StringComparer.Ordinal);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KebabCase();

    // A file that differs only in case, or goes by a known misspelling, is almost certainly the one meant.
    private static string[]? RenameHint(List<string> files, string expected, params string[] misspellings)
    {
        var found = files.FirstOrDefault(file =>
            string.Equals(file, expected, StringComparison.OrdinalIgnoreCase)
            || misspellings.Contains(file, StringComparer.OrdinalIgnoreCase));
        return found is null ? null : [$"Rename {found} to {expected}."];
    }

    private static DiscoveredAsset ExamineManifest(
        AssetKind kind,
        string name,
        string folder,
        string directory,
        string manifestFile,
        string text,
        Findings found)
    {
        var diagnostics = found.Diagnostics;
        var before = diagnostics.Count;
        var parsed = YamlDocument.Parse(text);
        if (parsed.Problem is { } problem)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, manifestFile, problem.Location, problem.Message, []));
            return new DiscoveredAsset(kind, name, folder, manifestFile, null);
        }

        var errors = SchemaValidator.Validate(parsed.Root, SchemaCatalog.Asset);
        foreach (var error in errors)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, manifestFile, Locate(parsed.Locations, error.Path), error.Message, error.Detail));
        }

        // Which block belongs is only clear once the manifest and its folder agree on the kind. When
        // they don't, the kind mismatch is the error worth reading, not a missing block.
        if (parsed.Root is JsonObject root && root["kind"] is JsonValue declared
            && declared.GetValueKind() == JsonValueKind.String && declared.GetValue<string>() == kind.ManifestName())
        {
            ExamineBlocks(kind, root, manifestFile, parsed.Locations, diagnostics);
        }

        if (errors.Count > 0)
        {
            return new DiscoveredAsset(kind, name, folder, manifestFile, null);
        }

        var manifest = parsed.Root!.AsObject();
        var declaredName = Text(manifest, "name");
        if (declaredName != name)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, manifestFile, Locate(parsed.Locations, "name"), $"name \"{declaredName}\" doesn't match its folder \"{name}\"", []));
        }

        var declaredKind = Text(manifest, "kind");
        if (declaredKind != kind.ManifestName())
        {
            diagnostics.Add(new Diagnostic(Severity.Error, manifestFile, Locate(parsed.Locations, "kind"), $"kind \"{declaredKind}\" doesn't match its folder \"{kind.Folder()}/\"", []));
        }

        // What a manifest points to is only worth checking once the manifest itself is valid.
        if (diagnostics.Count == before)
        {
            References.ExamineEvals(directory, folder, manifestFile, manifest, parsed.Locations, diagnostics);
            found.References.AddRange(References.Collect(manifestFile, manifest, parsed.Locations));
            found.Claims.Add(new MaturityClaim(manifestFile, Locate(parsed.Locations, "maturity"), kind, folder, Text(manifest, "maturity"), TrueEvals(manifest)));
        }

        return new DiscoveredAsset(kind, name, folder, manifestFile, Fields(kind, manifest));
    }

    private static void ExamineBlocks(
        AssetKind kind, JsonObject manifest, string manifestFile, IReadOnlyDictionary<string, SourceLocation> locations, List<Diagnostic> diagnostics)
    {
        foreach (var blockKind in AssetKinds.All)
        {
            if (SchemaCatalog.Block(blockKind) is not { } schema)
            {
                continue;
            }

            var name = blockKind.ManifestName();
            var present = manifest.TryGetPropertyValue(name, out var block);
            if (blockKind != kind)
            {
                if (present)
                {
                    diagnostics.Add(new Diagnostic(
                        Severity.Error, manifestFile, Locate(locations, name), $"Only {blockKind.Folder()} have a {name} block",
                        [$"This asset is in {kind.Folder()}/. Remove the block, or move the asset to {blockKind.Folder()}/."]));
                }
            }
            else if (!present)
            {
                var detail = schema["description"] is JsonValue description ? [description.GetValue<string>()] : Array.Empty<string>();
                diagnostics.Add(new Diagnostic(Severity.Error, manifestFile, Locate(locations, ""), $"Missing required field: {name}", detail));
            }
            else if (block is JsonObject)
            {
                // A block that isn't a mapping is already an error from the asset schema.
                foreach (var error in SchemaValidator.Validate(block, schema, name))
                {
                    diagnostics.Add(new Diagnostic(Severity.Error, manifestFile, Locate(locations, error.Path), error.Message, error.Detail));
                }
            }
        }
    }

    private static string Text(JsonObject manifest, string field) => manifest[field]!.GetValue<string>();

    private static AssetManifest Fields(AssetKind kind, JsonObject manifest) => new(
        Text(manifest, "maturity"),
        Text(manifest, "version"),
        manifest["supports"]!.AsObject().ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>(), StringComparer.Ordinal),
        Text(manifest, "description"),
        kind == AssetKind.Skill && manifest["skill"] is JsonObject skill && skill["use_when"] is JsonValue useWhen && useWhen.GetValueKind() == JsonValueKind.String ? useWhen.GetValue<string>() : null);

    private static IReadOnlySet<string> TrueEvals(JsonObject manifest) =>
        manifest["evals"] is JsonObject evals
            ? evals.Where(pair => pair.Value is JsonValue value && value.GetValueKind() == JsonValueKind.True).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    // A missing log means no usage yet. An unreadable one is an error, and the assets are still checked.
    private static IReadOnlyList<UsageEntry> ReadUsage(string vaultRoot, List<Diagnostic> diagnostics)
    {
        var path = Path.Combine(vaultRoot, UsageLog.File);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return UsageLog.Parse(File.ReadAllText(path));
        }
        catch (Exception problem) when (problem is not OutOfMemoryException)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, UsageLog.File, null, $"Couldn't read {UsageLog.File}: {problem.Message}", []));
            return [];
        }
    }

    // An error about a missing field has the parent's path, so walk up until a path has a location.
    internal static SourceLocation? Locate(IReadOnlyDictionary<string, SourceLocation> locations, string path)
    {
        while (true)
        {
            if (locations.TryGetValue(path, out var location))
            {
                return location;
            }

            if (path.Length == 0)
            {
                return null;
            }

            var cut = Math.Max(path.LastIndexOf('.'), path.LastIndexOf('['));
            path = cut < 0 ? "" : path[..cut];
        }
    }
}

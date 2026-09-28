using System.Text.Json;
using System.Text.Json.Nodes;
using Axiomarium.Core.Assets;
using Axiomarium.Core.Manifests;
using Axiomarium.Core.Schemas;

namespace Axiomarium.Core.Health;

/// <summary>Checks the health of a vault: finds every asset and validates its manifest. Reads, never writes.</summary>
public static class Doctor
{
    private const string ManifestName = "asset.yaml";

    /// <summary>Examines the vault at <paramref name="vaultRoot"/>.</summary>
    /// <param name="vaultRoot">The directory that holds <c>agents/</c>, <c>skills/</c> and the other kind folders.</param>
    /// <returns>
    /// A report, or the reason the doctor couldn't run: the folder doesn't exist, or none of the kind
    /// folders do. An empty kind folder is a vault with no assets, not a reason to stop.
    /// </returns>
    public static DoctorResult Run(string vaultRoot)
    {
        if (!Directory.Exists(vaultRoot))
        {
            return new DoctorResult(null, $"The folder {vaultRoot} does not exist.");
        }

        var kinds = AssetKinds.All.Where(kind => Directory.Exists(Path.Combine(vaultRoot, kind.Folder()))).ToList();
        if (kinds.Count == 0)
        {
            var folders = string.Join(", ", AssetKinds.All.Select(kind => kind.Folder() + "/"));
            return new DoctorResult(null, $"No vault found in {vaultRoot}. A vault has at least one of: {folders}.");
        }

        var assets = new List<DiscoveredAsset>();
        var diagnostics = new List<Diagnostic>();
        foreach (var kind in kinds)
        {
            var names = Directory.EnumerateDirectories(Path.Combine(vaultRoot, kind.Folder()))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => !name.StartsWith('.'))
                .Order(StringComparer.Ordinal);
            foreach (var name in names)
            {
                assets.Add(Examine(vaultRoot, kind, name, diagnostics));
            }
        }

        var ordered = diagnostics
            .OrderBy(diagnostic => diagnostic.File, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location?.Line ?? 0)
            .ToList();
        return new DoctorResult(new DoctorReport(assets, ordered), null);
    }

    private static DiscoveredAsset Examine(string vaultRoot, AssetKind kind, string name, List<Diagnostic> diagnostics)
    {
        var folder = $"{kind.Folder()}/{name}";
        var directory = Path.Combine(vaultRoot, kind.Folder(), name);

        // Names are compared exactly, because Windows and macOS would find agent.md as Agent.md and
        // Linux wouldn't, and the vault has to mean the same thing on every platform.
        var files = Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>().ToList();
        ExamineContent(kind, folder, directory, files, diagnostics);

        if (!files.Contains(ManifestName, StringComparer.Ordinal))
        {
            diagnostics.Add(new Diagnostic(Severity.Error, folder, null, $"Missing {ManifestName}", RenameHint(files, ManifestName, "asset.yml") ?? []));
            return new DiscoveredAsset(kind, name, folder, null, null, null);
        }

        var manifestFile = $"{folder}/{ManifestName}";

        // One unreadable or surprising manifest becomes a diagnostic on that file, so the rest of
        // the vault is still checked and the user learns which file is at fault.
        try
        {
            return ExamineManifest(kind, name, folder, manifestFile, File.ReadAllText(Path.Combine(directory, ManifestName)), diagnostics);
        }
        catch (Exception problem) when (problem is not OutOfMemoryException)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, manifestFile, null, $"Couldn't read {ManifestName}: {problem.Message}", []));
            return new DiscoveredAsset(kind, name, folder, manifestFile, null, null);
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

    // A file that differs only in case, or goes by a known misspelling, is almost certainly the one meant.
    private static string[]? RenameHint(List<string> files, string expected, params string[] misspellings)
    {
        var found = files.FirstOrDefault(file =>
            string.Equals(file, expected, StringComparison.OrdinalIgnoreCase)
            || misspellings.Contains(file, StringComparer.OrdinalIgnoreCase));
        return found is null ? null : [$"Rename {found} to {expected}."];
    }

    private static DiscoveredAsset ExamineManifest(
        AssetKind kind, string name, string folder, string manifestFile, string text, List<Diagnostic> diagnostics)
    {
        var parsed = YamlDocument.Parse(text);
        if (parsed.Problem is { } problem)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, manifestFile, problem.Location, problem.Message, []));
            return new DiscoveredAsset(kind, name, folder, manifestFile, null, null);
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
            return new DiscoveredAsset(kind, name, folder, manifestFile, null, null);
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

        return new DiscoveredAsset(kind, name, folder, manifestFile, Text(manifest, "maturity"), Text(manifest, "version"));
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

    // An error about a missing field has the parent's path, so walk up until a path has a location.
    private static SourceLocation? Locate(IReadOnlyDictionary<string, SourceLocation> locations, string path)
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

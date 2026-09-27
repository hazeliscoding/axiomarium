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
        var manifestPath = Path.Combine(directory, ManifestName);
        if (!File.Exists(manifestPath))
        {
            var hint = File.Exists(Path.Combine(directory, "asset.yml")) ? ["Rename asset.yml to asset.yaml."] : Array.Empty<string>();
            diagnostics.Add(new Diagnostic(Severity.Error, folder, null, $"Missing {ManifestName}", hint));
            return new DiscoveredAsset(kind, name, folder, null, null, null);
        }

        var manifestFile = $"{folder}/{ManifestName}";

        // One unreadable or surprising manifest becomes a diagnostic on that file, so the rest of
        // the vault is still checked and the user learns which file is at fault.
        try
        {
            return ExamineManifest(kind, name, folder, manifestFile, File.ReadAllText(manifestPath), diagnostics);
        }
        catch (Exception problem) when (problem is not OutOfMemoryException)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, manifestFile, null, $"Couldn't read {ManifestName}: {problem.Message}", []));
            return new DiscoveredAsset(kind, name, folder, manifestFile, null, null);
        }
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

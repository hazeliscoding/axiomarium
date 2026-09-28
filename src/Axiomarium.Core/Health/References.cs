using System.Text.Json;
using System.Text.Json.Nodes;
using Axiomarium.Core.Assets;
using Axiomarium.Core.Manifests;

namespace Axiomarium.Core.Health;

/// <summary>A manifest's pointer to another asset, such as a policy rule's <c>enforced_by</c> entry.</summary>
/// <param name="File">The manifest that points, relative to the vault root.</param>
/// <param name="Location">Where in the manifest.</param>
/// <param name="Target">The asset's folder, such as <c>agents/determinism-auditor</c>.</param>
internal sealed record Reference(string File, SourceLocation? Location, string Target);

/// <summary>Checks that what a valid manifest points to exists: eval files, and the assets its policy rules name.</summary>
internal static class References
{
    /// <summary>Adds an error for each <c>evals.&lt;type&gt;: true</c> whose <c>evals/&lt;type&gt;/</c> folder has no files.</summary>
    public static void ExamineEvals(
        string directory,
        string folder,
        string manifestFile,
        JsonObject manifest,
        IReadOnlyDictionary<string, SourceLocation> locations,
        List<Diagnostic> diagnostics)
    {
        if (manifest["evals"] is not JsonObject evals)
        {
            return;
        }

        foreach (var (type, flag) in evals)
        {
            if (flag is JsonValue value && value.GetValueKind() == JsonValueKind.True && !HasEvalFiles(Path.Combine(directory, "evals", type)))
            {
                diagnostics.Add(new Diagnostic(
                    Severity.Error,
                    manifestFile,
                    Doctor.Locate(locations, $"evals.{type}"),
                    $"evals.{type} is true, but {folder}/evals/{type}/ has no files",
                    [$"Add the {type} evals there, or set evals.{type} to false."]));
            }
        }
    }

    /// <summary>The assets a manifest's policy rules name in <c>enforced_by</c>.</summary>
    public static IEnumerable<Reference> Collect(string manifestFile, JsonObject manifest, IReadOnlyDictionary<string, SourceLocation> locations)
    {
        if (manifest["policy"]?["rules"] is not JsonArray rules)
        {
            yield break;
        }

        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i]?["enforced_by"] is not JsonArray targets)
            {
                continue;
            }

            for (var j = 0; j < targets.Count; j++)
            {
                var path = $"policy.rules[{i}].enforced_by[{j}]";
                yield return new Reference(manifestFile, Doctor.Locate(locations, path), targets[j]!.GetValue<string>());
            }
        }
    }

    /// <summary>Adds an error for each reference whose target isn't an asset in <paramref name="assets"/>.</summary>
    public static void ExamineTargets(IReadOnlyList<DiscoveredAsset> assets, IEnumerable<Reference> references, List<Diagnostic> diagnostics)
    {
        foreach (var reference in references)
        {
            if (assets.Any(asset => asset.Folder == reference.Target))
            {
                continue;
            }

            // The schema already holds the target to kind-folder/name, so the kind folder is known.
            var kindFolder = reference.Target[..reference.Target.IndexOf('/')];
            var names = assets.Where(asset => asset.Kind.Folder() == kindFolder).Select(asset => asset.Name).ToList();
            var detail = names.Count == 0 ? $"The vault has no {kindFolder} yet." : $"The vault's {kindFolder}: {string.Join(", ", names)}";
            diagnostics.Add(new Diagnostic(
                Severity.Error, reference.File, reference.Location, $"enforced_by names \"{reference.Target}\", which doesn't exist", [detail]));
        }
    }

    // Hidden files such as .gitkeep only keep a folder in git; they aren't evals.
    private static bool HasEvalFiles(string directory) =>
        Directory.Exists(directory)
        && Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any(file => !Path.GetFileName(file).StartsWith('.'));
}

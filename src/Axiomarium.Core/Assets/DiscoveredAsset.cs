namespace Axiomarium.Core.Assets;

/// <summary>An asset folder the doctor found in the vault.</summary>
/// <param name="Kind">The kind its top-level folder says it is.</param>
/// <param name="Name">Its folder name.</param>
/// <param name="Folder">Its folder, relative to the vault root, with forward slashes.</param>
/// <param name="ManifestFile">Its <c>asset.yaml</c>, relative to the vault root, or <see langword="null"/> when there is none.</param>
/// <param name="Manifest">
/// What its manifest says, when the manifest passes the asset schema. <see langword="null"/> when there
/// is no manifest, or it can't be read or doesn't pass, and then the report has an error for it.
/// </param>
public sealed record DiscoveredAsset(AssetKind Kind, string Name, string Folder, string? ManifestFile, AssetManifest? Manifest);

/// <summary>The fields of a manifest that passes the asset schema, which are therefore all present.</summary>
/// <param name="Maturity">Its maturity, such as <c>experimental</c>.</param>
/// <param name="Version">Its SemVer version.</param>
/// <param name="Supports">Each harness it supports, such as <c>claude-code</c>, and how well: <c>full</c>, <c>partial</c> or <c>experimental</c>.</param>
/// <param name="Description">What the asset is, from its <c>description</c>.</param>
/// <param name="UseWhen">
/// A skill's <c>skill.use_when</c>: when it should activate. <see langword="null"/> for other kinds, and for
/// a skill whose block is missing or invalid, which the report has an error for.
/// </param>
/// <param name="Hook">
/// A hook's <c>hook</c> block: when it runs and what. <see langword="null"/> for other kinds, and for a hook whose
/// block is missing or invalid, which the report has an error for.
/// </param>
public sealed record AssetManifest(
    string Maturity, string Version, IReadOnlyDictionary<string, string> Supports, string Description, string? UseWhen = null, HookBlock? Hook = null);

/// <summary>When a hook runs and what it runs, from its <c>hook</c> block.</summary>
/// <param name="Event">The event it runs on: <c>session-start</c>, <c>before-edit</c> or <c>after-edit</c>.</param>
/// <param name="Command">The command the harness runs, such as <c>axm hook scope-sheriff</c>.</param>
public sealed record HookBlock(string Event, string Command);

namespace Axiomarium.Core.Assets;

/// <summary>An asset folder the doctor found in the vault.</summary>
/// <param name="Kind">The kind its top-level folder says it is.</param>
/// <param name="Name">Its folder name.</param>
/// <param name="Folder">Its folder, relative to the vault root, with forward slashes.</param>
/// <param name="ManifestFile">Its <c>asset.yaml</c>, relative to the vault root, or <see langword="null"/> when there is none.</param>
/// <param name="Maturity">The manifest's maturity, when the manifest is valid.</param>
/// <param name="Version">The manifest's version, when the manifest is valid.</param>
public sealed record DiscoveredAsset(AssetKind Kind, string Name, string Folder, string? ManifestFile, string? Maturity, string? Version);

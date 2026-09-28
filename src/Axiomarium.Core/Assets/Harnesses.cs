using Axiomarium.Core.Schemas;

namespace Axiomarium.Core.Assets;

/// <summary>The harnesses an asset can support, as its manifest's <c>supports</c> names them.</summary>
public static class Harnesses
{
    /// <summary>Every harness, in the asset schema's order, such as <c>claude-code</c>. The schema is the source.</summary>
    public static IReadOnlyList<string> All { get; } =
        [.. SchemaCatalog.Asset["properties"]!["supports"]!["properties"]!.AsObject().Select(pair => pair.Key)];

    /// <summary>The assets that are of <paramref name="kind"/> and support <paramref name="harness"/>.</summary>
    /// <param name="assets">The assets to filter.</param>
    /// <param name="kind">Only this kind, or every kind when <see langword="null"/>.</param>
    /// <param name="harness">Only assets that support this harness, or all when <see langword="null"/>.</param>
    /// <returns>
    /// The matching assets, in their order. An asset without manifest fields matches any harness, because
    /// what it supports is unknown, and leaving it out would hide it.
    /// </returns>
    public static IReadOnlyList<DiscoveredAsset> Filter(IEnumerable<DiscoveredAsset> assets, AssetKind? kind, string? harness) =>
        [.. assets.Where(asset =>
            (kind is null || asset.Kind == kind)
            && (harness is null || asset.Manifest is null || asset.Manifest.Supports.ContainsKey(harness)))];
}

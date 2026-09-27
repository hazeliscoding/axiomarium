namespace Axiomarium.Core.Assets;

/// <summary>The names each <see cref="AssetKind"/> goes by in the vault.</summary>
public static class AssetKinds
{
    /// <summary>Every kind, in the order the vault lists them.</summary>
    public static IReadOnlyList<AssetKind> All { get; } =
        [AssetKind.Agent, AssetKind.Skill, AssetKind.Hook, AssetKind.Policy, AssetKind.Workflow, AssetKind.Experiment];

    /// <summary>The top-level folder the kind lives in, such as <c>agents</c>.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The folder name, without a slash.</returns>
    public static string Folder(this AssetKind kind) => kind switch
    {
        AssetKind.Agent => "agents",
        AssetKind.Skill => "skills",
        AssetKind.Hook => "hooks",
        AssetKind.Policy => "policies",
        AssetKind.Workflow => "workflows",
        AssetKind.Experiment => "experiments",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The value a manifest's <c>kind</c> field has for the kind, such as <c>agent</c>.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The lowercase singular name.</returns>
    public static string ManifestName(this AssetKind kind) => kind switch
    {
        AssetKind.Agent => "agent",
        AssetKind.Skill => "skill",
        AssetKind.Hook => "hook",
        AssetKind.Policy => "policy",
        AssetKind.Workflow => "workflow",
        AssetKind.Experiment => "experiment",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}

internal static class NetworkProbe
{
    internal static string Probe() => new HttpClient().BaseAddress?.ToString() ?? "";
}

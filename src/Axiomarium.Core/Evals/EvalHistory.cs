using System.Globalization;
using System.Text.Json;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Core.Evals;

/// <summary>One side of a comparison: a case's runs on one harness with one version of the asset, counted.</summary>
/// <param name="Passed">How many runs passed.</param>
/// <param name="Runs">How many runs there were.</param>
/// <param name="Input">Input tokens.</param>
/// <param name="Cached">Cached input tokens.</param>
/// <param name="Output">Output tokens.</param>
/// <param name="Seconds">Wall time in seconds.</param>
/// <param name="ToolCalls">Tool calls.</param>
/// <param name="Turns">Turns, where the harness reports them.</param>
/// <param name="Cost">Cost in US dollars, where the harness reports it.</param>
/// <param name="Judge">How many runs the judge passed, of how many it judged, or <see langword="null"/>. Model judgment.</param>
public sealed record CaseSide(
    int Passed, int Runs, Spread? Input, Spread? Cached, Spread? Output, Spread? Seconds, Spread? ToolCalls, Spread? Turns, Spread? Cost, (int Passed, int Judged)? Judge)
{
    /// <summary>The side a fresh run's summary makes.</summary>
    /// <param name="summary">The case's runs on one harness.</param>
    /// <returns>Its counts and spreads.</returns>
    public static CaseSide Of(CaseSummary summary) => new(
        summary.Passed, summary.Runs, summary.Input, summary.Cached, summary.Output, summary.Seconds, summary.ToolCalls, summary.Turns, summary.Cost,
        summary.Judge is { } judge ? (judge.Passed, judge.Judged) : null);
}

/// <summary>A case's runs on one harness, as a saved run recorded them.</summary>
/// <param name="Case">The case's name.</param>
/// <param name="Type">Its type: <c>behavioral</c> or <c>regression</c>.</param>
/// <param name="Harness">The harness.</param>
/// <param name="CaseHash">The case's content hash when it ran.</param>
/// <param name="Side">Its counts and spreads.</param>
public sealed record SavedCase(string Case, string Type, Harness Harness, string? CaseHash, CaseSide Side);

/// <summary>A run saved in <c>.axm/evals/</c>, read back.</summary>
/// <param name="File">The file it was read from.</param>
/// <param name="Date">When it finished.</param>
/// <param name="Ref">For a compare's baseline, the git ref it ran, or <c>none</c>; otherwise <see langword="null"/>.</param>
/// <param name="Asset">The asset's folder.</param>
/// <param name="AssetHash">The asset's content hash when it ran, <c>none</c> for a baseline without the asset.</param>
/// <param name="Sealed">Whether it ran in a sealed home.</param>
/// <param name="Harnesses">Each harness it ran on: its version and the model <c>axm</c> asked it for.</param>
/// <param name="Cases">Each case on each harness.</param>
public sealed record SavedRun(
    string File,
    DateTimeOffset Date,
    string? Ref,
    string Asset,
    string? AssetHash,
    bool Sealed,
    IReadOnlyList<(Harness Harness, string Version, string? Asked)> Harnesses,
    IReadOnlyList<SavedCase> Cases);

/// <summary>Reads the runs <c>axm eval</c> saved, so <c>axm eval compare</c> can reuse a baseline that still describes the files as they are.</summary>
public static class EvalHistory
{
    /// <summary>Reads a saved run.</summary>
    /// <param name="json">The file's contents, as <c>axm eval run --json</c> or a compare wrote them.</param>
    /// <param name="file">The file's path, kept for the report.</param>
    /// <returns>The run, or <see langword="null"/> when the text isn't a saved run of shape 1 with one asset.</returns>
    public static SavedRun? Read(string json, string file)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Number(root, "schemaVersion") != 1
                || Text(root, "command") is not ("eval run" or "eval compare")
                || !DateTimeOffset.TryParse(Text(root, "date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() != 1)
            {
                return null;
            }

            var asset = assets[0];
            var harnesses = Items(root, "harnesses")
                .Select(item => (Harness: HarnessNamed(Text(item, "harness")), Version: Text(item, "version") ?? "", Asked: Text(item, "asked")))
                .Where(item => item.Harness is not null)
                .Select(item => (item.Harness!.Value, item.Version, item.Asked))
                .ToList();
            var cases = Items(asset, "cases")
                .Select(item => (Item: item, Harness: HarnessNamed(Text(item, "harness"))))
                .Where(item => item.Harness is not null && Text(item.Item, "case") is not null)
                .Select(item => new SavedCase(Text(item.Item, "case")!, Text(item.Item, "type") ?? "behavioral", item.Harness!.Value, Text(item.Item, "hash"), Side(item.Item)))
                .ToList();
            return new SavedRun(file, date, Text(root, "ref"), Text(asset, "asset") ?? "", Text(asset, "hash"), Text(root, "home") == "sealed", harnesses, cases);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The newest saved run a compare can use as its baseline instead of running it again.</summary>
    /// <param name="folder">The asset's history folder, such as <c>.axm/evals/skills/deploy</c>.</param>
    /// <param name="asset">The asset's folder, such as <c>skills/deploy</c>.</param>
    /// <param name="assetHash">The baseline's content hash, or <c>none</c> for a baseline without the asset.</param>
    /// <param name="caseHashes">Each case the compare runs, by name and type, with its content hash now.</param>
    /// <param name="harnesses">Each harness the compare runs on, with its version and the model it will be asked for.</param>
    /// <param name="runs">How many runs each case gets.</param>
    /// <returns>
    /// The newest run in a sealed home with the same asset hash, whose harnesses have the same versions and asked
    /// models, and whose every case on every harness has the same hash and as many runs; or <see langword="null"/>.
    /// </returns>
    public static SavedRun? FindBaseline(
        string folder,
        string asset,
        string assetHash,
        IReadOnlyDictionary<(string Case, string Type), string> caseHashes,
        IReadOnlyDictionary<Harness, (string Version, string? Asked)> harnesses,
        int runs)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        return Directory.EnumerateFiles(folder, "*.json")
            .Select(file => Read(File.ReadAllText(file), file))
            .OfType<SavedRun>()
            .Where(run => run.Sealed && run.Asset == asset && run.AssetHash == assetHash)
            .Where(run => harnesses.All(harness => run.Harnesses.Any(saved =>
                saved.Harness == harness.Key && saved.Version == harness.Value.Version && saved.Asked == harness.Value.Asked)))
            .Where(run => caseHashes.All(expected => harnesses.Keys.All(harness => run.Cases.Any(saved =>
                saved.Case == expected.Key.Case && saved.Type == expected.Key.Type && saved.Harness == harness
                && saved.CaseHash == expected.Value && saved.Side.Runs == runs))))
            .OrderByDescending(run => run.Date)
            .FirstOrDefault();
    }

    private static CaseSide Side(JsonElement item)
    {
        var stats = item.TryGetProperty("stats", out var value) ? value : default;
        (int, int)? judge = item.TryGetProperty("judge", out var judged) && judged.ValueKind == JsonValueKind.Object
            ? ((int)(Number(judged, "passed") ?? 0), (int)(Number(judged, "judged") ?? 0))
            : null;
        return new CaseSide(
            (int)(Number(item, "passed") ?? 0), (int)(Number(item, "runs") ?? 0),
            Spread(stats, "inputTokens"), Spread(stats, "cachedTokens"), Spread(stats, "outputTokens"), Spread(stats, "seconds"),
            Spread(stats, "toolCalls"), Spread(stats, "turns"), Spread(stats, "cost"), judge);
    }

    private static Spread? Spread(JsonElement stats, string name) =>
        stats.ValueKind == JsonValueKind.Object && stats.TryGetProperty(name, out var spread) && spread.ValueKind == JsonValueKind.Object
            ? new Spread(Number(spread, "median") ?? 0, Number(spread, "min") ?? 0, Number(spread, "max") ?? 0)
            : null;

    private static Harness? HarnessNamed(string? name) => name == Harness.ClaudeCode.Name() ? Harness.ClaudeCode : name == Harness.Codex.Name() ? Harness.Codex : null;

    private static IEnumerable<JsonElement> Items(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray() : [];

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}

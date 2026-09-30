using System.Security.Cryptography;
using System.Text;
using Axiomarium.Core.Assets;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Core.Evals;

/// <summary>How one eval session turned out.</summary>
/// <param name="Spec">What ran: the asset, case, harness and run.</param>
/// <param name="Record">What the harness recorded, or <see langword="null"/> when the session couldn't start.</param>
/// <param name="Checks">Each check's result, in the case's order.</param>
/// <param name="Elapsed">Its wall time, measured by <c>axm</c>, from the harness's start to its end.</param>
/// <param name="Stopped">Why it ended before finishing, such as a timeout or the turn cap, or <see langword="null"/> when it finished.</param>
public sealed record EvalSessionResult(EvalSessionSpec Spec, SessionRecord? Record, IReadOnlyList<CheckResult> Checks, TimeSpan Elapsed, string? Stopped)
{
    /// <summary>Whether the run passed: it finished, and every check held.</summary>
    public bool Passed => Stopped is null && Checks.All(check => check.Passed);

    /// <summary>The absolute paths outside the session's copy that its commands named, in the order they first appear.</summary>
    public IReadOnlyList<string> Outside { get; init; } = [];
}

/// <summary>The median and range of a measure over a case's runs.</summary>
/// <param name="Median">The median: the middle value, or the mean of the two middle ones.</param>
/// <param name="Min">The smallest value.</param>
/// <param name="Max">The largest value.</param>
public sealed record Spread(double Median, double Min, double Max);

/// <summary>A check that didn't hold in some of a case's runs.</summary>
/// <param name="Check">The check.</param>
/// <param name="Runs">The runs it failed in, in order.</param>
/// <param name="Observed">What it found in the first of them.</param>
public sealed record CheckFailure(EvalCheck Check, IReadOnlyList<int> Runs, string Observed);

/// <summary>A case's runs on one harness, counted.</summary>
/// <param name="Asset">The asset under test.</param>
/// <param name="Case">The case.</param>
/// <param name="Harness">The harness.</param>
/// <param name="Passed">How many runs passed.</param>
/// <param name="Runs">How many runs there were, stopped ones included.</param>
/// <param name="Failures">Each check that failed in any run, in the case's order.</param>
/// <param name="Stops">Each run that ended before finishing, and why.</param>
/// <param name="Input">Input tokens, over the runs the harness counted.</param>
/// <param name="Cached">Cached input tokens, over the same runs.</param>
/// <param name="Output">Output tokens, over the same runs.</param>
/// <param name="Seconds">Wall time in seconds, over every run.</param>
/// <param name="ToolCalls">Tool calls, over the runs that have a record.</param>
/// <param name="Turns">Turns, where the harness reports them: Claude Code only.</param>
/// <param name="Cost">Cost in US dollars, where the harness reports it: Claude Code only.</param>
public sealed record CaseSummary(
    DiscoveredAsset Asset,
    EvalCase Case,
    Harness Harness,
    int Passed,
    int Runs,
    IReadOnlyList<CheckFailure> Failures,
    IReadOnlyList<(int Run, string Reason)> Stops,
    Spread? Input,
    Spread? Cached,
    Spread? Output,
    Spread Seconds,
    Spread? ToolCalls,
    Spread? Turns,
    Spread? Cost)
{
    /// <summary>The slowest tool call over the runs, and its run, or <see langword="null"/> when no run timed its calls.</summary>
    public (int Run, TimedCall Call)? Slowest { get; init; }

    /// <summary>Each run whose commands named paths outside its copy, with the paths.</summary>
    public IReadOnlyList<(int Run, IReadOnlyList<string> Paths)> Outside { get; init; } = [];
}

/// <summary>Counts an eval run's results by case and harness, with no verdict: counts, medians and ranges only.</summary>
public static class EvalSummaries
{
    /// <summary>Summarizes <paramref name="results"/>.</summary>
    /// <param name="results">Every session's result.</param>
    /// <returns>One summary per asset, case and harness, in the order the results first name them.</returns>
    public static IReadOnlyList<CaseSummary> Summarize(IReadOnlyList<EvalSessionResult> results) =>
    [
        .. results
            .GroupBy(result => (result.Spec.Asset.Folder, result.Spec.Case.Type, result.Spec.Case.Name, result.Spec.Harness))
            .Select(group => Summary([.. group.OrderBy(result => result.Spec.Run)])),
    ];

    private static CaseSummary Summary(List<EvalSessionResult> runs)
    {
        var first = runs[0].Spec;
        var counted = runs.Where(run => run.Record?.Tokens is not null).Select(run => run.Record!).ToList();
        var failures = first.Case.Checks
            .Select((check, index) => (Check: check, Index: index, Failed: runs.Where(run => run.Checks.Count > index && !run.Checks[index].Passed).ToList()))
            .Where(item => item.Failed.Count > 0)
            .Select(item => new CheckFailure(item.Check, [.. item.Failed.Select(run => run.Spec.Run)], item.Failed[0].Checks[item.Index].Observed))
            .ToList();
        return new CaseSummary(
            first.Asset,
            first.Case,
            first.Harness,
            runs.Count(run => run.Passed),
            runs.Count,
            failures,
            [.. runs.Where(run => run.Stopped is not null).Select(run => (run.Spec.Run, run.Stopped!))],
            Of(counted.Select(record => (double)record.Tokens!.Input)),
            Of(counted.Select(record => (double)record.Tokens!.Cached)),
            Of(counted.Select(record => (double)record.Tokens!.Output)),
            Of(runs.Select(run => run.Elapsed.TotalSeconds))!,
            Of(runs.Where(run => run.Record is not null).Select(run => (double)run.Record!.ToolCalls)),
            Of(counted.Where(record => record.Turns is not null).Select(record => (double)record.Turns!.Value)),
            Of(counted.Where(record => record.Cost is not null).Select(record => record.Cost!.Value)))
        {
            Slowest = runs
                .SelectMany(run => (run.Record?.Calls ?? []).Select(call => (run.Spec.Run, Call: call)))
                .OrderByDescending(item => item.Call.Seconds)
                .Select(item => ((int, TimedCall)?)item)
                .FirstOrDefault(),
            Outside = [.. runs.Where(run => run.Outside.Count > 0).Select(run => (run.Spec.Run, run.Outside))],
        };
    }

    private static Spread? Of(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0)
        {
            return null;
        }

        var middle = sorted.Count / 2;
        var median = sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        return new Spread(median, sorted[0], sorted[^1]);
    }
}

/// <summary>
/// Content hashes of an asset and its cases, which history keeps so <c>axm eval compare</c> knows when a saved run
/// still describes the files as they are.
/// </summary>
public static class EvalHashes
{
    /// <summary>The hash of an asset's files, its <c>evals</c> folder left out.</summary>
    /// <param name="folder">The asset's folder, absolute.</param>
    /// <returns><c>sha256:</c> and the hex hash of each file's path and content, in path order, with <c>\r\n</c> read as <c>\n</c>.</returns>
    public static string Asset(string folder) => Hash(folder, path => !path.StartsWith("evals/", StringComparison.Ordinal));

    /// <summary>The hash of a case's files: its <c>eval.yaml</c> and its <c>repo/</c>.</summary>
    /// <param name="folder">The case's folder, absolute.</param>
    /// <returns><c>sha256:</c> and the hex hash, made the way <see cref="Asset"/> makes it.</returns>
    public static string Case(string folder) => Hash(folder, _ => true);

    // A path and its content are each followed by a NUL, which neither can hold, so no two layouts hash alike.
    private static string Hash(string folder, Func<string, bool> include)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => (File: file, Path: Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/')))
            .Where(file => include(file.Path))
            .OrderBy(file => file.Path, StringComparer.Ordinal);
        foreach (var (file, path) in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path + "\0"));
            hash.AppendData(Encoding.UTF8.GetBytes(File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal) + "\0"));
        }

        return "sha256:" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

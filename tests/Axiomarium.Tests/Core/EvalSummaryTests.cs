using Axiomarium.Core.Assets;
using Axiomarium.Core.Evals;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class EvalSummaryTests
{
    private static readonly DiscoveredAsset Asset = new(
        AssetKind.Skill, "deploy", "skills/deploy", "skills/deploy/asset.yaml", new AssetManifest("experimental", "0.1.0", new Dictionary<string, string>(), "Deploys."));

    private static readonly EvalCheck Validates = new(CheckKind.Run, "axm validate");
    private static readonly EvalCheck Loads = new(CheckKind.Loaded, "deploy");
    private static readonly EvalCase Ships = new("ships", EvalType.Behavioral, "Ship it.", [], [Validates, Loads], null, null);

    private static EvalSessionResult Result(Harness harness, int run, bool validated, long input, double seconds, string? stopped = null, int? turns = 10) => new(
        new EvalSessionSpec(Asset, Ships, "/vault/skills/deploy/evals/behavioral/ships", harness, run),
        stopped is null ? new SessionRecord(new SessionActivity(["deploy"], [], "Shipped."), new TokenCount(input, input / 2, 100), 5, turns, turns is null ? null : 0.10, 0, null, null, null) : null,
        [new CheckResult(Validates, validated, validated ? "exited 0" : "exited 1"), new CheckResult(Loads, true, "loaded")],
        TimeSpan.FromSeconds(seconds),
        stopped);

    [Fact]
    public void A_run_passes_when_it_finished_and_every_check_held()
    {
        Assert.True(Result(Harness.ClaudeCode, 1, true, 1000, 30).Passed);
        Assert.False(Result(Harness.ClaudeCode, 1, false, 1000, 30).Passed);
        Assert.False(Result(Harness.ClaudeCode, 1, true, 1000, 600, stopped: "it ran past the 10-minute timeout").Passed);
    }

    [Fact]
    public void Each_case_and_harness_gets_its_pass_count_and_the_median_and_range_of_each_measure()
    {
        EvalSessionResult[] results =
        [
            Result(Harness.ClaudeCode, 1, true, 1000, 30), Result(Harness.ClaudeCode, 2, false, 3000, 50), Result(Harness.ClaudeCode, 3, true, 2000, 40),
            Result(Harness.Codex, 1, true, 4000, 20, turns: null), Result(Harness.Codex, 2, true, 6000, 10, turns: null),
        ];

        var summaries = EvalSummaries.Summarize(results);

        Assert.Equal([Harness.ClaudeCode, Harness.Codex], summaries.Select(summary => summary.Harness));
        var claude = summaries[0];
        Assert.Equal((2, 3), (claude.Passed, claude.Runs));
        Assert.Equal(new Spread(2000, 1000, 3000), claude.Input);
        Assert.Equal(new Spread(1000, 500, 1500), claude.Cached);
        Assert.Equal(new Spread(40, 30, 50), claude.Seconds);
        Assert.Equal(new Spread(10, 10, 10), claude.Turns);
        var codex = summaries[1];
        Assert.Equal((new Spread(5000, 4000, 6000), (Spread?)null, (Spread?)null), (codex.Input, codex.Turns, codex.Cost));
    }

    // A stopped session has no counts, so it's left out of the spreads but counted in the runs.
    [Fact]
    public void Failed_checks_and_stops_are_listed_by_run()
    {
        EvalSessionResult[] results =
        [
            Result(Harness.ClaudeCode, 1, false, 1000, 30), Result(Harness.ClaudeCode, 2, true, 3000, 50),
            Result(Harness.ClaudeCode, 3, false, 2000, 40), Result(Harness.ClaudeCode, 4, true, 0, 600, stopped: "it ran past the 10-minute timeout"),
        ];

        var summary = Assert.Single(EvalSummaries.Summarize(results));

        Assert.Equal((1, 4), (summary.Passed, summary.Runs));
        var failure = Assert.Single(summary.Failures);
        Assert.Equal((Validates, "exited 1"), (failure.Check, failure.Observed));
        Assert.Equal([1, 3], failure.Runs);
        Assert.Equal([(4, "it ran past the 10-minute timeout")], summary.Stops);
        Assert.Equal(new Spread(2000, 1000, 3000), summary.Input);
        Assert.Equal(new Spread(45, 30, 600), summary.Seconds);
    }

    [Fact]
    public void The_slowest_call_and_each_run_that_left_its_copy_are_named()
    {
        var slow = Result(Harness.Codex, 1, true, 1000, 300) with { Outside = [@"C:\ai\axiomarium\README.md"] };
        slow = slow with { Record = slow.Record! with { Calls = [new TimedCall("git --version", 141.4), new TimedCall("axm validate", 2)] } };
        var quick = Result(Harness.Codex, 2, true, 1000, 30);
        quick = quick with { Record = quick.Record! with { Calls = [new TimedCall("pwd", 1)] } };

        var summary = Assert.Single(EvalSummaries.Summarize([slow, quick]));

        Assert.Equal((1, new TimedCall("git --version", 141.4)), summary.Slowest);
        Assert.Equal([(1, (IReadOnlyList<string>)[@"C:\ai\axiomarium\README.md"])], summary.Outside.Select(item => (item.Run, item.Paths)).ToList(), new OutsideComparer());
    }

    private sealed class OutsideComparer : IEqualityComparer<(int Run, IReadOnlyList<string> Paths)>
    {
        public bool Equals((int Run, IReadOnlyList<string> Paths) x, (int Run, IReadOnlyList<string> Paths) y) => x.Run == y.Run && x.Paths.SequenceEqual(y.Paths);

        public int GetHashCode((int Run, IReadOnlyList<string> Paths) item) => item.Run;
    }

    [Fact]
    public void A_hash_changes_with_the_content_and_not_with_line_ends_or_the_evals_folder()
    {
        using var vault = new TempVault()
            .Write("skills/deploy/asset.yaml", "name: deploy\n")
            .Write("skills/deploy/skill.md", "# Deploy\n")
            .Write("skills/deploy/evals/behavioral/ships/eval.yaml", "prompt: Ship it.\n");
        var asset = Path.Combine(vault.Root, "skills", "deploy");
        var ships = Path.Combine(asset, "evals", "behavioral", "ships");

        var before = (EvalHashes.Asset(asset), EvalHashes.Case(ships));
        vault.Write("skills/deploy/evals/behavioral/ships/eval.yaml", "prompt: Ship it now.\n");
        var caseChanged = (EvalHashes.Asset(asset), EvalHashes.Case(ships));
        vault.Write("skills/deploy/skill.md", "# Deploy\r\n");
        var lineEnds = EvalHashes.Asset(asset);
        vault.Write("skills/deploy/skill.md", "# Deploy it\n");

        Assert.StartsWith("sha256:", before.Item1);
        Assert.Equal(before.Item1, caseChanged.Item1);
        Assert.NotEqual(before.Item2, caseChanged.Item2);
        Assert.Equal(before.Item1, lineEnds);
        Assert.NotEqual(before.Item1, EvalHashes.Asset(asset));
    }
}

using Axiomarium.Core.Evals;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class EvalHistoryTests
{
    private static string Saved(string date, string assetHash = "sha256:aa", string caseHash = "sha256:c1", string version = "2.1.285 (Claude Code)", string? asked = "opus", int runs = 3) => $$"""
        {
          "schemaVersion": 1,
          "command": "eval compare",
          "variant": "baseline",
          "ref": "HEAD",
          "date": "{{date}}",
          "home": "sealed",
          "harnesses": [ { "harness": "claude-code", "version": "{{version}}", "model": "claude-opus-5-5", "asked": {{(asked is null ? "null" : $"\"{asked}\"")}} } ],
          "assets": [ {
            "asset": "skills/deploy", "hash": "{{assetHash}}",
            "cases": [ {
              "case": "ships", "type": "behavioral", "hash": "{{caseHash}}", "harness": "claude-code", "passed": 2, "runs": {{runs}},
              "stats": {
                "inputTokens": { "median": 1000, "min": 900, "max": 1100 }, "cachedTokens": { "median": 500, "min": 400, "max": 600 },
                "outputTokens": { "median": 100, "min": 90, "max": 110 }, "seconds": { "median": 30, "min": 20, "max": 40 },
                "toolCalls": { "median": 5, "min": 4, "max": 6 }, "turns": null, "cost": { "median": 0.1, "min": 0.1, "max": 0.1 }
              },
              "judge": { "passed": 1, "judged": 3 },
              "sessions": []
            } ]
          } ]
        }
        """;

    private static readonly Dictionary<(string Case, string Type), string> Cases = new() { [("ships", "behavioral")] = "sha256:c1" };
    private static readonly Dictionary<Harness, (string Version, string? Asked)> Harnesses = new() { [Harness.ClaudeCode] = ("2.1.285 (Claude Code)", "opus") };

    private static TempVault History(params (string Name, string Json)[] files) =>
        files.Aggregate(new TempVault(), (vault, file) => vault.Write($"skills/deploy/{file.Name}", file.Json));

    private static SavedRun? Find(TempVault history, int runs = 3) =>
        EvalHistory.FindBaseline(Path.Combine(history.Root, "skills", "deploy"), "skills/deploy", "sha256:aa", Cases, Harnesses, runs);

    [Fact]
    public void A_saved_run_reads_back_as_each_case_s_counts_and_spreads()
    {
        var run = EvalHistory.Read(Saved("2026-09-30T03:23:07Z"), "saved.json")!;

        Assert.Equal((new DateTimeOffset(2026, 9, 30, 3, 23, 7, TimeSpan.Zero), "HEAD"), (run.Date, run.Ref));
        var side = Assert.Single(run.Cases);
        Assert.Equal(("ships", "behavioral", Harness.ClaudeCode, "sha256:c1"), (side.Case, side.Type, side.Harness, side.CaseHash));
        Assert.Equal((2, 3, new Spread(1000, 900, 1100), (Spread?)null, (1, 3)), (side.Side.Passed, side.Side.Runs, side.Side.Input, side.Side.Turns, side.Side.Judge));
    }

    // A reused baseline must be the same asset and cases, on the same harness version and model, and as many runs.
    [Fact]
    public void A_baseline_is_reused_only_when_everything_it_depends_on_matches_and_the_newest_wins()
    {
        using var matching = History(("20260929-1000-baseline.json", Saved("2026-09-29T10:00:00Z")), ("20260930-0323-baseline.json", Saved("2026-09-30T03:23:07Z")));
        using var otherAsset = History(("a.json", Saved("2026-09-30T03:23:07Z", assetHash: "sha256:bb")));
        using var otherCase = History(("a.json", Saved("2026-09-30T03:23:07Z", caseHash: "sha256:c2")));
        using var otherVersion = History(("a.json", Saved("2026-09-30T03:23:07Z", version: "2.1.286 (Claude Code)")));
        using var otherModel = History(("a.json", Saved("2026-09-30T03:23:07Z", asked: "sonnet")));

        Assert.Equal("20260930-0323-baseline.json", Path.GetFileName(Find(matching)!.File));
        Assert.Null(Find(matching, runs: 5));
        Assert.All(new[] { otherAsset, otherCase, otherVersion, otherModel }, history => Assert.Null(Find(history)));
    }

    [Fact]
    public void Files_that_aren_t_saved_runs_are_passed_over()
    {
        using var history = History(("notes.json", "{ \"not\": \"a run\" }"), ("broken.json", "{"), ("a.json", Saved("2026-09-30T03:23:07Z")));

        Assert.Equal("a.json", Path.GetFileName(Find(history)!.File));
        Assert.Null(EvalHistory.FindBaseline(Path.Combine(history.Root, "nowhere"), "skills/deploy", "sha256:aa", Cases, Harnesses, 3));
    }
}

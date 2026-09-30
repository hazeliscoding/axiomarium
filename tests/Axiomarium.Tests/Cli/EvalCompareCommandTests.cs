using System.Text.Json;
using Axiomarium.Cli;

namespace Axiomarium.Tests.Cli;

// Git and the harnesses are scripted, so no test reads a real repo's history or runs a model.
public class EvalCompareCommandTests
{
    private const string Folder = "skills/agent-asset-authoring";

    private const string Case = """
        prompt: Add a hook that warns when a migration file changes.
        allow:
          - axm validate
        checks:
          - loaded: agent-asset-authoring
          - run: axm validate

        """;

    private static readonly string Manifest = TempVault.Manifest("skill", "agent-asset-authoring");

    private static string[] Fixture(string name) => File.ReadAllLines(Path.Combine(RepoRoot.Path, "tests", "Axiomarium.Tests", "Fixtures", "evals", name));

    private static TempVault Vault() => new TempVault()
        .Folder("repo/.git")
        .Write($"repo/{Folder}/asset.yaml", Manifest)
        .Write($"repo/{Folder}/skill.md", "# Authoring\n")
        .Write($"repo/{Folder}/evals/behavioral/new-hook/eval.yaml", Case)
        .Write("home/.claude/.credentials.json", "{}")
        .Write("home/.claude/settings.json", "{\"model\":\"opus\"}")
        .Folder("scratch");

    // HEAD holds the asset with an older skill.md, unless the test says it's the same.
    private static Func<HarnessCall, HarnessOutput> Respond(string oldBody = "# Old authoring")
    {
        var atHead = new Dictionary<string, string>
        {
            [$"{Folder}/asset.yaml"] = Manifest,
            [$"{Folder}/skill.md"] = oldBody + "\n",
            [$"{Folder}/evals/behavioral/new-hook/eval.yaml"] = Case,
        };
        return call => (call.Command, call.Arguments.FirstOrDefault()) switch
        {
            (_, "--version") => FakeRunner.Version(call),
            ("git", "ls-tree") => new HarnessOutput(true, [.. atHead.Keys], 0, ""),
            ("git", "show") => new HarnessOutput(true, atHead[call.Arguments[1]["HEAD:".Length..]].TrimEnd('\n').Split('\n'), 0, ""),
            ("claude", _) => new HarnessOutput(true, Fixture(Installed(call)?.Contains("Old", StringComparison.Ordinal) == true ? "claude-code-subagent.jsonl" : "claude-code-task.jsonl"), 0, ""),
            _ => new HarnessOutput(true, [], 0, ""),
        };
    }

    private static string? Installed(HarnessCall call)
    {
        var skill = Path.Combine(call.Folder!, ".claude", "skills", "agent-asset-authoring", "SKILL.md");
        return File.Exists(skill) ? File.ReadAllText(skill) : null;
    }

    private static (int ExitCode, string Output, string Error, FakeRunner Runner) Compare(TempVault vault, Func<HarnessCall, HarnessOutput> respond, params string[] flags)
    {
        var runner = new FakeRunner(respond) { FolderRoot = Path.Combine(vault.Root, "scratch") };
        var (exitCode, output, error) = CliRun.Run(
            ["eval", "compare", "agent-asset-authoring", .. flags.Contains("--runs") ? Array.Empty<string>() : ["--runs", "1"], "--harness", "claude-code", .. flags],
            machine: TestMachine.For(vault.Root),
            currentDirectory: Path.Combine(vault.Root, "repo"),
            runner: runner,
            clock: new FixedClock());
        return (exitCode, output.ReplaceLineEndings("\n"), error, runner);
    }

    [Fact]
    public void Shows_each_case_s_baseline_and_candidate_side_by_side_with_the_change()
    {
        using var vault = Vault();

        var (exitCode, output, error, _) = Compare(vault, Respond());

        Assert.Equal((AxmCli.Passed, ""), (exitCode, error));
        Assert.Equal(
            """
            AXM EVAL COMPARE // skills/agent-asset-authoring · HEAD against the working tree · 1 case × 1 run · 1 harness · 2 sessions, 4 at a time
              Baseline and candidate sessions alternate, each in a sealed home with your logins and model, and nothing else of your setup.

              CLAUDE CODE 2.1.284 // claude-opus-5-5
              01  new-hook            baseline                candidate               change
                  passed            0 of 1                  1 of 1                  +1
                  input tokens      101,147                 238,714                 +137,567
                  cached tokens     76,039                  228,398                 +152,359
                  output tokens     2,656                   2,512                   -144
                  time              0 s                     0 s                     0
                  tool calls        4                       12                      +8
                  turns             3                       14                      +11
                  cost              $0.22                   $0.18                   -$0.04

            Each run is the harness's model at work, so a rerun can differ, and 1 run a side is a small sample.
            Saved to .axm/evals/skills/agent-asset-authoring/20260928-120000-baseline.json
            Saved to .axm/evals/skills/agent-asset-authoring/20260928-120000-candidate.json
            baseline passed 0 of 1 run · candidate passed 1 of 1

            """,
            output);
    }

    // A median with its range can be wider than a column, as a real compare showed, so columns fit their widest value.
    [Fact]
    public void Columns_fit_a_median_and_its_range()
    {
        using var vault = Vault();
        var runs = 0;
        var respond = Respond();
        HarnessOutput Varied(HarnessCall call) =>
            call.Command == "claude" && call.Arguments[0] != "--version" && Installed(call)?.Contains("Old", StringComparison.Ordinal) != true
                ? new HarnessOutput(true, Fixture(Interlocked.Increment(ref runs) % 2 == 0 ? "claude-code-task.jsonl" : "claude-code-hooks.jsonl"), 0, "")
                : respond(call);

        var (_, output, _, _) = Compare(vault, Varied, "--runs", "2");

        var row = output.Split('\n').Single(line => line.TrimStart().StartsWith("input tokens", StringComparison.Ordinal));
        Assert.Matches(@"^      input tokens +\S+( \([^)]*\))? {2,}\S+ \([^)]*\) {2,}\S", row);
    }

    [Fact]
    public void An_asset_unchanged_since_the_ref_has_nothing_to_compare()
    {
        using var vault = Vault();

        var (exitCode, _, error, runner) = Compare(vault, Respond(oldBody: "# Authoring"));

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.Contains("skills/agent-asset-authoring is unchanged since HEAD.", error, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Calls, call => call.Command == "claude" && call.Arguments[0] != "--version");
    }

    [Fact]
    public void A_baseline_of_none_runs_the_cases_without_the_asset()
    {
        using var vault = Vault();

        var (exitCode, output, _, runner) = Compare(vault, Respond(), "--baseline", "none");

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Contains("no asset against the working tree", output, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Calls, call => call.Command == "git" && call.Arguments[0] is "ls-tree" or "show");
        var sessions = runner.Calls.Where(call => call.Command == "claude" && call.Arguments[0] != "--version").ToList();
        Assert.Equal(2, sessions.Count);
    }

    // A saved baseline that still describes the files, harness and model is reused, unless --fresh says otherwise.
    [Fact]
    public void A_second_compare_reuses_the_saved_baseline_and_fresh_runs_it_again()
    {
        using var vault = Vault();
        Compare(vault, Respond());

        var (_, reused, _, reusing) = Compare(vault, Respond());
        var (_, _, _, rerunning) = Compare(vault, Respond(), "--fresh");

        Assert.Contains(
            "  The baseline reuses the run saved 2026-09-28 12:00 UTC in .axm/evals/skills/agent-asset-authoring/20260928-120000-baseline.json.\n",
            reused,
            StringComparison.Ordinal);
        Assert.Contains("      passed            0 of 1                  1 of 1                  +1\n", reused, StringComparison.Ordinal);
        Assert.Single(reusing.Calls, call => call.Command == "claude" && call.Arguments[0] != "--version");
        Assert.Equal(2, rerunning.Calls.Count(call => call.Command == "claude" && call.Arguments[0] != "--version"));
    }

    [Fact]
    public void Json_sets_each_case_s_two_sides_side_by_side()
    {
        using var vault = Vault();

        var (exitCode, output, _, _) = Compare(vault, Respond(), "--json");

        Assert.Equal(AxmCli.Passed, exitCode);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        Assert.Equal(("eval compare", "HEAD", false), (root.GetProperty("command").GetString(), root.GetProperty("baseline").GetProperty("ref").GetString(), root.GetProperty("casesChanged").GetBoolean()));
        var compared = root.GetProperty("cases")[0];
        Assert.Equal((0, 1), (compared.GetProperty("baseline").GetProperty("passed").GetInt32(), compared.GetProperty("candidate").GetProperty("passed").GetInt32()));
        Assert.Equal(238_714, compared.GetProperty("candidate").GetProperty("stats").GetProperty("inputTokens").GetProperty("median").GetDouble());
    }
}

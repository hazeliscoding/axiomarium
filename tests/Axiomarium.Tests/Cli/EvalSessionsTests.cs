using System.Runtime.InteropServices;
using Axiomarium.Cli;
using Axiomarium.Core.Evals;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Judging;

namespace Axiomarium.Tests.Cli;

// Sessions replay the M5 spike's streams, so no test runs a model.
public class EvalSessionsTests
{
    private const string Case = """
        prompt: Add a hook that warns when a migration file changes.
        allow:
          - axm validate
        checks:
          - loaded: agent-asset-authoring
          - ran: axm validate
          - run: axm validate
          - file: app.ts

        """;

    private static readonly Dictionary<string, string?> Environment = new()
    {
        ["PATH"] = "/usr/bin",
        ["CLAUDECODE"] = "1",
        ["CLAUDE_CODE_SESSION_ID"] = "parent",
        ["CLAUDE_EFFORT"] = "high",
        ["HOME"] = "/home/dev",
    };

    private static string[] Fixture(string name) => File.ReadAllLines(Path.Combine(RepoRoot.Path, "tests", "Axiomarium.Tests", "Fixtures", "evals", name));

    private static TempVault Vault() => new TempVault()
        .Folder("repo/.git")
        .Write("repo/skills/agent-asset-authoring/asset.yaml", TempVault.Manifest("skill", "agent-asset-authoring").Replace("  claude-code: experimental\n", "  claude-code: experimental\n  codex: experimental\n"))
        .Write("repo/skills/agent-asset-authoring/skill.md", "# Authoring\n")
        .Write("repo/skills/agent-asset-authoring/evals/behavioral/new-hook/eval.yaml", Case)
        .Write("repo/skills/agent-asset-authoring/evals/behavioral/new-hook/repo/app.ts", "export const a = 1;\n")
        .Write("home/.claude/.credentials.json", "{\"secret\":1}")
        .Write("home/.claude/settings.json", "{\"model\":\"opus\"}")
        .Write("home/.codex/auth.json", "{\"secret\":2}")
        .Folder("scratch");

    private sealed record Seen(HarnessCall Call, bool LoginPresent, bool SkillInstalled);

    private static (FakeRunner Runner, List<Seen> Seen) Runner(TempVault vault, int? claudeExit = 0)
    {
        var seen = new List<Seen>();
        var runner = new FakeRunner(call =>
        {
            lock (seen)
            {
                var home = call.Environment?.GetValueOrDefault("CLAUDE_CONFIG_DIR");
                seen.Add(new Seen(
                    call,
                    home is not null && File.Exists(Path.Combine(home, ".credentials.json")),
                    call.Folder is not null && (File.Exists(Path.Combine(call.Folder, ".claude", "skills", "agent-asset-authoring", "SKILL.md"))
                        || File.Exists(Path.Combine(call.Folder, ".agents", "skills", "agent-asset-authoring", "SKILL.md")))));
            }

            return call.Command switch
            {
                "claude" => new HarnessOutput(true, Fixture("claude-code-task.jsonl"), claudeExit, ""),
                "codex" when call.Input.StartsWith("Run the shell command git --version", StringComparison.Ordinal) => new HarnessOutput(true, Fixture("codex-sandbox-refusals.jsonl"), 0, ""),
                "codex" => new HarnessOutput(true, Fixture("codex-task.jsonl"), 0, ""),
                _ => new HarnessOutput(true, ["2 assets · 0 errors"], 0, ""),
            };
        })
        { FolderRoot = Path.Combine(vault.Root, "scratch") };
        return (runner, seen);
    }

    private static EvalSessionsRun Run(TempVault vault, FakeRunner runner, OSPlatform? platform = null, Harness[]? harnesses = null)
    {
        var plan = EvalRuns.Plan(Path.Combine(vault.Root, "repo"), [], harnesses ?? [Harness.ClaudeCode, Harness.Codex], 1, TestMachine.For(vault.Root)).Plan!;
        return EvalSessions.RunAsync(
            runner, plan.Sessions, plan.VaultRoot, TestMachine.For(vault.Root), Environment, Path.Combine(vault.Root, "bin", "axm.exe"), null, TimeProvider.System, platform ?? OSPlatform.Windows)
            .GetAwaiter().GetResult();
    }

    [Fact]
    public void Each_session_runs_in_its_own_copy_with_the_asset_installed_and_its_checks_decided()
    {
        using var vault = Vault();
        var (runner, seen) = Runner(vault);

        var run = Run(vault, runner);

        Assert.Null(run.Problem);
        Assert.Equal([(Harness.ClaudeCode, true), (Harness.Codex, true)], run.Results.Select(result => (result.Spec.Harness, result.Passed)));
        Assert.All(seen.Where(item => item.Call.Command is "claude" || (item.Call.Command == "codex" && item.Call.Input.StartsWith("Add", StringComparison.Ordinal))), item => Assert.True(item.SkillInstalled));
        Assert.All(run.Results, result => Assert.Equal("app.ts matches", result.Checks.Single(check => check.Check.Kind == CheckKind.File).Observed));
        Assert.Equal(3, seen.Count(item => item.Call.Command == "git" && item.Call.Folder == seen.First(first => first.Call.Command == "claude").Call.Folder));
    }

    // The home, the borrowed logins and the copies are gone afterwards, and the login was there while the session ran.
    [Fact]
    public void Sessions_run_in_the_sealed_home_without_the_parent_session_s_variables_and_nothing_is_left()
    {
        using var vault = Vault();
        var (runner, seen) = Runner(vault);

        Run(vault, runner);

        var claude = seen.Single(item => item.Call.Command == "claude");
        Assert.True(claude.LoginPresent);
        var variables = claude.Call.Environment!;
        Assert.EndsWith(Path.Combine("home", ".claude"), variables["CLAUDE_CONFIG_DIR"]);
        Assert.EndsWith(Path.Combine("home", ".codex"), variables["CODEX_HOME"]);
        Assert.Equal(variables["HOME"], variables["USERPROFILE"]);
        Assert.Equal(((string?)null, (string?)null, (string?)null), (variables["CLAUDECODE"], variables["CLAUDE_CODE_SESSION_ID"], variables["CLAUDE_EFFORT"]));
        Assert.StartsWith(Path.Combine(vault.Root, "bin") + Path.PathSeparator, variables["PATH"]);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(vault.Root, "scratch")));
    }

    [Fact]
    public void Each_harness_runs_headless_holding_the_session_to_the_copy_and_the_case_s_commands()
    {
        using var vault = Vault();
        var (runner, seen) = Runner(vault);

        Run(vault, runner);

        var claude = seen.Single(item => item.Call.Command == "claude").Call;
        Assert.Equal(
            ["-p", "--output-format", "stream-json", "--verbose", "--no-session-persistence", "--permission-mode", "acceptEdits", "--strict-mcp-config", "--max-turns", "50",
                "--allowedTools", "Bash(axm validate:*)", "--allowedTools", "PowerShell(axm validate:*)"],
            claude.Arguments);
        Assert.Equal("Add a hook that warns when a migration file changes.", claude.Input);
        var codex = seen.Last(item => item.Call.Command == "codex").Call;
        Assert.Equal(["exec", "--json", "-s", "workspace-write", "--skip-git-repo-check", "--ignore-rules", "-"], codex.Arguments);
    }

    // On Windows a new Codex home's first sandboxed command stalls, so one unscored session pays for it first.
    [Fact]
    public void On_windows_one_unscored_codex_session_warms_the_sandbox_first()
    {
        using var vault = Vault();
        var (runner, seen) = Runner(vault);
        using var linuxVault = Vault();
        var (linuxRunner, linuxSeen) = Runner(linuxVault);

        var run = Run(vault, runner);
        var linux = Run(linuxVault, linuxRunner, OSPlatform.Linux, [Harness.Codex]);

        Assert.StartsWith("Run the shell command git --version", seen.First(item => item.Call.Command == "codex").Call.Input, StringComparison.Ordinal);
        Assert.Equal(new TokenCount(55_554, 27_520, 590), run.Warmup!.Tokens);
        Assert.Single(run.Results, result => result.Spec.Harness == Harness.Codex);
        Assert.Null(linux.Warmup);
        Assert.DoesNotContain(linuxSeen, item => item.Call.Input.StartsWith("Run the shell command", StringComparison.Ordinal));
    }

    [Fact]
    public void A_session_stopped_by_the_timeout_is_a_failed_run_that_says_so()
    {
        using var vault = Vault();
        var (runner, _) = Runner(vault, claudeExit: null);

        var result = Run(vault, runner, harnesses: [Harness.ClaudeCode]).Results.Single();

        Assert.Equal(("it ran past the 10-minute timeout", false), (result.Stopped, result.Passed));
    }

    // A compare runs each session with one version of the asset: the baseline's files, or none at all.
    [Fact]
    public async Task A_session_installs_the_version_it_names_or_none()
    {
        using var vault = Vault()
            .Write("baseline/skills/agent-asset-authoring/asset.yaml", TempVault.Manifest("skill", "agent-asset-authoring"))
            .Write("baseline/skills/agent-asset-authoring/skill.md", "# The old body\n");
        var installed = new List<string?>();
        var runner = new FakeRunner(call =>
        {
            if (call.Command == "claude")
            {
                var skill = Path.Combine(call.Folder!, ".claude", "skills", "agent-asset-authoring", "SKILL.md");
                lock (installed)
                {
                    installed.Add(File.Exists(skill) ? File.ReadAllText(skill)[(File.ReadAllText(skill).IndexOf("---\n#", StringComparison.Ordinal) + 4)..] : null);
                }

                return new HarnessOutput(true, Fixture("claude-code-task.jsonl"), 0, "");
            }

            return new HarnessOutput(true, [], 0, "");
        })
        { FolderRoot = Path.Combine(vault.Root, "scratch") };
        var plan = EvalRuns.Plan(Path.Combine(vault.Root, "repo"), [], [Harness.ClaudeCode], 1, TestMachine.For(vault.Root)).Plan!;
        var session = plan.Sessions.Single();
        var baselineRoot = Path.Combine(vault.Root, "baseline");
        var baseline = Axiomarium.Core.Health.Doctor.Run(baselineRoot).Report!.Assets.Single();
        EvalSessionSpec[] sessions =
        [
            session with { Variant = new EvalVariant("baseline", baseline, baselineRoot) },
            session with { Run = 2, Variant = new EvalVariant("baseline", null, null) },
            session with { Run = 3, Variant = new EvalVariant("candidate", session.Asset, plan.VaultRoot) },
        ];

        await EvalSessions.RunAsync(runner, sessions, plan.VaultRoot, TestMachine.For(vault.Root), Environment, Path.Combine(vault.Root, "bin", "axm.exe"), null, TimeProvider.System, OSPlatform.Linux);

        // The sessions run in parallel, so their order isn't fixed.
        Assert.Equal(3, installed.Count);
        Assert.Contains("# The old body\n", installed);
        Assert.Contains("# Authoring\n", installed);
        Assert.Contains(null, installed);
    }

    // The judge grades what the session did against the rubric, and its verdict never changes what the checks decided.
    [Fact]
    public async Task A_rubric_is_graded_in_the_sealed_home_from_the_copy_s_diff_apart_from_the_checks()
    {
        using var vault = Vault().Write(
            "repo/skills/agent-asset-authoring/evals/behavioral/new-hook/eval.yaml", Case + "judge:\n  rubric: The hook warns and never blocks.\n");
        var judged = new List<HarnessCall>();
        var runner = new FakeRunner(call =>
        {
            if (call.Command == "claude" && call.Arguments.Contains("--tools"))
            {
                lock (judged)
                {
                    judged.Add(call);
                }

                return FakeRunner.ClaudeAnswer("""{"passed": false, "reason": "It blocks the edit."}""");
            }

            return call switch
            {
                { Command: "claude" } => new HarnessOutput(true, Fixture("claude-code-task.jsonl"), 0, ""),
                { Command: "codex" } => new HarnessOutput(true, Fixture("codex-task.jsonl"), 0, ""),
                { Command: "git", Arguments: ["diff", ..] } => new HarnessOutput(true, ["+response: block"], 0, ""),
                _ => new HarnessOutput(true, [], 0, ""),
            };
        })
        { FolderRoot = Path.Combine(vault.Root, "scratch") };

        var plan = EvalRuns.Plan(Path.Combine(vault.Root, "repo"), [], [Harness.Codex], 1, TestMachine.For(vault.Root)).Plan!;
        var result = (await EvalSessions.RunAsync(
            runner, plan.Sessions, plan.VaultRoot, TestMachine.For(vault.Root), Environment, Path.Combine(vault.Root, "bin", "axm.exe"), null, TimeProvider.System, OSPlatform.Linux, Harness.ClaudeCode))
            .Results.Single();

        Assert.Equal((new RubricVerdict(false, "It blocks the edit."), true), (result.Judged, result.Passed));
        var brief = Assert.Single(judged);
        Assert.Contains("The hook warns and never blocks.", brief.Input, StringComparison.Ordinal);
        Assert.Contains("+response: block", brief.Input, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("home", ".claude"), brief.Environment!["CLAUDE_CONFIG_DIR"]);
    }

    // A real run lost its timeout this way: what fails after the harness ran mustn't hide why the session stopped.
    [Fact]
    public void A_failure_after_the_session_keeps_why_it_stopped()
    {
        using var vault = Vault();
        var runner = new FakeRunner(call => call.Command switch
        {
            "claude" => new HarnessOutput(true, Fixture("claude-code-task.jsonl"), null, ""),
            "git" => new HarnessOutput(true, [], 0, ""),
            _ => throw new IOException("The process cannot access the file."),
        })
        { FolderRoot = Path.Combine(vault.Root, "scratch") };

        var result = Run(vault, runner, harnesses: [Harness.ClaudeCode]).Results.Single();

        Assert.Equal("it ran past the 10-minute timeout", result.Stopped);
        Assert.NotNull(result.Record);
    }

    [Fact]
    public void Without_a_login_nothing_runs_and_nothing_is_left()
    {
        using var vault = Vault();
        File.Delete(Path.Combine(vault.Root, "home", ".claude", ".credentials.json"));
        var (runner, seen) = Runner(vault);

        var run = Run(vault, runner);

        Assert.Equal(("No Claude Code login in ~/.claude/.credentials.json.", "Log in to Claude Code first."), (run.Problem, run.Hint));
        Assert.Empty(seen);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(vault.Root, "scratch")));
    }

    // A folder whose process is gone is a leftover. One whose process runs is another run in progress.
    [Fact]
    public void Leftovers_are_the_folders_whose_process_is_gone()
    {
        using var scratch = new TempVault()
            .Write("axm-evals/20260929-dead/axm.pid", int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Write("axm-evals/20260929-dead/home/.claude/.credentials.json", "{}")
            .Write("axm-evals/20260929-unowned/copy-1/a.txt", "a");
        var runner = new ProcessHarnessRunner(scratch.Root);
        var live = runner.CreateFolder("evals");

        var removed = runner.RemoveLeftovers("evals");

        Assert.Equal(["20260929-dead", "20260929-unowned"], removed);
        Assert.True(Directory.Exists(live));
    }
}

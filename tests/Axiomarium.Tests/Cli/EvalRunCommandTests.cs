using System.Text.Json;
using Axiomarium.Cli;

namespace Axiomarium.Tests.Cli;

// Sessions replay the M5 spike's streams, so no test runs a model.
public class EvalRunCommandTests
{
    private const string Case = """
        prompt: Add a hook that warns when a migration file changes.
        allow:
          - axm validate
        checks:
          - loaded: agent-asset-authoring
          - run: axm validate
          - file: hooks/*/hook.md

        """;

    private static string[] Fixture(string name) => File.ReadAllLines(Path.Combine(RepoRoot.Path, "tests", "Axiomarium.Tests", "Fixtures", "evals", name));

    private static TempVault Vault(bool cases = true)
    {
        var vault = new TempVault()
            .Folder("repo/.git")
            .Write("repo/skills/agent-asset-authoring/asset.yaml", TempVault.Manifest("skill", "agent-asset-authoring").Replace("  claude-code: experimental\n", "  claude-code: experimental\n  codex: experimental\n"))
            .Write("repo/skills/agent-asset-authoring/skill.md", "# Authoring\n")
            .Write("home/.claude/.credentials.json", "{}")
            .Write("home/.claude/settings.json", "{\"model\":\"opus\"}")
            .Write("home/.codex/auth.json", "{}")
            .Write("home/.codex/config.toml", "model = \"gpt-6-sol\"\n")
            .Folder("scratch");
        return cases ? vault.Write("repo/skills/agent-asset-authoring/evals/behavioral/new-hook/eval.yaml", Case) : vault;
    }

    // The agent's work: the fixture sessions wrote a hook, which the check commands stand in for here.
    private static HarnessOutput Respond(HarnessCall call)
    {
        if (call.Command is "claude" or "codex" && call.Arguments[0] != "--version" && call.Folder is not null && !call.Input.StartsWith("Run the shell command", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.Combine(call.Folder, "hooks", "migration-guard"));
            File.WriteAllText(Path.Combine(call.Folder, "hooks", "migration-guard", "hook.md"), "# Migration guard\n");
        }

        return (call.Command, call.Arguments[0]) switch
        {
            (_, "--version") => FakeRunner.Version(call),
            ("claude", _) => new HarnessOutput(true, Fixture("claude-code-task.jsonl"), 0, ""),
            ("codex", _) => new HarnessOutput(true, Fixture(call.Input.StartsWith("Run the shell command", StringComparison.Ordinal) ? "codex-sandbox-refusals.jsonl" : "codex-task.jsonl"), 0, ""),
            _ => new HarnessOutput(true, ["2 assets · 0 errors"], 0, ""),
        };
    }

    private static (int ExitCode, string Output, string Error) Eval(TempVault vault, Func<HarnessCall, HarnessOutput> respond, params string[] flags) =>
        CliRun.Run(
            ["eval", "run", "--runs", "1", .. flags],
            machine: TestMachine.For(vault.Root),
            currentDirectory: Path.Combine(vault.Root, "repo"),
            runner: new FakeRunner(respond) { FolderRoot = Path.Combine(vault.Root, "scratch") },
            clock: new FixedClock());

    [Fact]
    public void Reports_each_case_s_passed_runs_and_what_they_used_and_saves_the_run()
    {
        using var vault = Vault();

        var (exitCode, output, error) = Eval(vault, Respond, "--harness", "claude-code");

        Assert.Equal((AxmCli.Passed, ""), (exitCode, error));
        Assert.Equal(
            """
            AXM EVAL RUN // 1 asset · 1 case × 1 run · 1 harness · 1 session, 4 at a time
              Each session runs in a sealed home with your logins and model, and nothing else of your setup.

              CLAUDE CODE 2.1.284 // claude-opus-5-5
              01  agent-asset-authoring // new-hook           passed in 1 of 1 run
                  tokens  238,714 in, 228,398 cached · 2,512 out
                  work    0 s · 12 tool calls · 14 turns · $0.18

            Each run is the harness's model at work, so a rerun can differ.
            Saved to .axm/evals/skills/agent-asset-authoring/20260928-120000.json
            1 run · 1 passed · 0 failed

            """.ReplaceLineEndings("\n"),
            output.ReplaceLineEndings("\n"));
    }

    // History is saved output, so the file is what --json printed for that asset.
    [Fact]
    public void Json_is_the_whole_run_and_the_history_file_is_the_same_json()
    {
        using var vault = Vault();

        var (exitCode, output, _) = Eval(vault, Respond, "--json");

        Assert.Equal(AxmCli.Passed, exitCode);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        Assert.Equal((1, "eval run", "sealed"), (root.GetProperty("schemaVersion").GetInt32(), root.GetProperty("command").GetString(), root.GetProperty("home").GetString()));
        Assert.Equal(["claude-code", "codex"], root.GetProperty("harnesses").EnumerateArray().Select(harness => harness.GetProperty("harness").GetString()));
        Assert.Equal("gpt-6-sol", root.GetProperty("harnesses")[1].GetProperty("model").GetString());
        Assert.Equal(OperatingSystem.IsWindows(), root.TryGetProperty("warmup", out _));
        var asset = root.GetProperty("assets")[0];
        Assert.Equal("skills/agent-asset-authoring", asset.GetProperty("asset").GetString());
        Assert.StartsWith("sha256:", asset.GetProperty("hash").GetString(), StringComparison.Ordinal);
        var codex = asset.GetProperty("cases")[1];
        Assert.Equal(("new-hook", "behavioral", "codex", 1, 1), (codex.GetProperty("case").GetString(), codex.GetProperty("type").GetString(), codex.GetProperty("harness").GetString(), codex.GetProperty("passed").GetInt32(), codex.GetProperty("runs").GetInt32()));
        var session = codex.GetProperty("sessions")[0];
        Assert.Equal(250_374, session.GetProperty("tokens").GetProperty("input").GetInt64());
        Assert.Equal(JsonValueKind.Null, session.GetProperty("turns").ValueKind);
        Assert.Equal(
            ["loaded agent-asset-authoring", "axm validate exits 0", "hooks/*/hook.md exists"],
            session.GetProperty("checks").EnumerateArray().Select(check => check.GetProperty("expects").GetString()));
        var history = File.ReadAllText(Path.Combine(vault.Root, "repo", ".axm", "evals", "skills", "agent-asset-authoring", "20260928-120000.json"));
        Assert.Equal(output, history);
    }

    // Codex's sandbox reads the whole disk on Windows, and a real run read this repo's own hooks for the answer.
    [Fact]
    public void A_run_that_read_outside_its_copy_is_named_with_the_paths()
    {
        using var vault = Vault();
        var roaming = new HarnessOutput(
            true,
            [
                """{"type":"system","subtype":"init","model":"claude-opus-5-5","claude_code_version":"2.1.285","skills":[]}""",
                """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Skill","input":{"skill":"agent-asset-authoring"}}]}}""",
                """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"cat C:\\ai\\axiomarium\\hooks\\scope-sheriff\\hook.md; cat /home/dev/notes.md"}}]}}""",
                """{"type":"result","subtype":"success","is_error":false,"result":"Done.","num_turns":3,"total_cost_usd":0.01,"modelUsage":{}}""",
            ],
            0,
            "");

        var (_, output, _) = Eval(vault, call => call.Command == "claude" && call.Arguments[0] != "--version" ? Written(call, roaming) : Respond(call), "--harness", "claude-code");
        var (_, json, _) = Eval(vault, call => call.Command == "claude" && call.Arguments[0] != "--version" ? Written(call, roaming) : Respond(call), "--harness", "claude-code", "--json");

        Assert.Contains(
            @"      --  run 1 read outside its copy: C:\ai\axiomarium\hooks\scope-sheriff\hook.md, /home/dev/notes.md",
            output.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            [@"C:\ai\axiomarium\hooks\scope-sheriff\hook.md", "/home/dev/notes.md"],
            document.RootElement.GetProperty("assets")[0].GetProperty("cases")[0].GetProperty("sessions")[0].GetProperty("outside").EnumerateArray().Select(path => path.GetString()));
    }

    private static HarnessOutput Written(HarnessCall call, HarnessOutput output)
    {
        Respond(call);
        return output;
    }

    [Fact]
    public void A_harness_that_isn_t_installed_is_skipped_and_said_so()
    {
        using var vault = Vault();

        var (exitCode, output, _) = Eval(vault, call => call.Command == "codex" ? new HarnessOutput(false, [], null, "codex isn't on PATH.") : Respond(call));

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Contains("  CODEX // skipped: codex isn't on PATH.\n", output.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("1 run · 1 passed · 0 failed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_vault_without_cases_can_t_run()
    {
        using var vault = Vault(cases: false);

        var (exitCode, _, error) = Eval(vault, Respond);

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.Contains("No asset in the vault has eval cases yet.", error, StringComparison.Ordinal);
    }
}

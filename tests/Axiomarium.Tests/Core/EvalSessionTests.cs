using Axiomarium.Core.Evals;

namespace Axiomarium.Tests.Core;

// The fixtures are the M5 spike's streams, trimmed, with placeholder ids and paths.
public class EvalSessionTests
{
    private const string Copy = @"C:\axm-evals\run\copy-1";

    private static string[] Fixture(string name) => File.ReadAllLines(Path.Combine(RepoRoot.Path, "tests", "Axiomarium.Tests", "Fixtures", "evals", name));

    private static TempVault Rollouts(params string[] names) =>
        names.Aggregate(new TempVault(), (vault, name) => vault.Write($"sessions/2026/09/29/rollout-{name}", string.Join('\n', Fixture(name)) + "\n"));

    [Fact]
    public void A_claude_code_session_gives_its_loads_commands_reply_and_what_it_cost()
    {
        var session = ClaudeCodeSessions.Read(Fixture("claude-code-task.jsonl"));

        Assert.Equal(["agent-asset-authoring"], session.Activity.Loads);
        // Three commands were denied, so they never ran.
        Assert.Equal(["axm list", "axm validate", "axm list"], session.Activity.Commands);
        Assert.StartsWith("I added a hook asset called `migration-change-warning`.", session.Activity.Reply);
        Assert.Equal((12, 14, 3), (session.ToolCalls, session.Turns, session.Denials));
        Assert.Equal(0.1783676, session.Cost!.Value, 6);
        Assert.Equal(new TokenCount(238_714, 228_398, 2_512), session.Tokens);
        Assert.Equal(("claude-opus-5-5", "2.1.285", (string?)null), (session.Model, session.Version, session.Stopped));
    }

    // usage covers only the main thread; modelUsage adds each subagent's tokens.
    [Fact]
    public void A_claude_code_subagent_is_a_load_and_its_tokens_count()
    {
        var session = ClaudeCodeSessions.Read(Fixture("claude-code-subagent.jsonl"));

        Assert.Equal(["determinism-auditor"], session.Activity.Loads);
        Assert.Equal(new TokenCount(101_147, 76_039, 2_656), session.Tokens);
        Assert.Equal(4, session.ToolCalls);
    }

    [Fact]
    public void A_claude_code_session_that_stops_early_says_why()
    {
        var lines = Fixture("claude-code-hooks.jsonl");
        var capped = lines.Select(line => line.Replace("\"subtype\":\"success\"", "\"subtype\":\"error_max_turns\"", StringComparison.Ordinal));

        Assert.Equal("it reached the turn cap", ClaudeCodeSessions.Read(capped).Stopped);
        var cut = ClaudeCodeSessions.Read(lines.Take(lines.Length - 1));
        Assert.Equal(("it ended without a result", (TokenCount?)null, (string?)null), (cut.Stopped, cut.Tokens, cut.Activity.Reply));
    }

    // Codex wraps every command in its shell, and a doubled backslash in the program's path on Windows.
    [Fact]
    public void A_codex_session_gives_its_commands_unwrapped_and_the_skills_it_read()
    {
        var session = CodexSessions.Read(Fixture("codex-task.jsonl"), Copy, @"C:\Users\dev", null);

        Assert.Equal(["agent-asset-authoring"], session.Activity.Loads);
        Assert.Equal(18, session.Activity.Commands.Count);
        Assert.Equal(
            ["Get-Content .agents/skills/agent-asset-authoring/SKILL.md", "Get-ChildItem -Force | Select-Object Name,Mode"],
            session.Activity.Commands.Take(2));
        Assert.Contains("axm validate", session.Activity.Commands);
        Assert.StartsWith("Added the [migration warning hook]", session.Activity.Reply);
        Assert.Equal((new TokenCount(250_374, 233_600, 3_191), 19), (session.Tokens, session.ToolCalls));
        Assert.Equal(((int?)null, (double?)null, (string?)null), (session.Turns, session.Cost, session.Stopped));
    }

    [Theory]
    [InlineData("\"C:\\\\Program Files\\\\PowerShell\\\\pwsh.exe\" -NoProfile -Command \"curl.exe -s -w '%{http_code}' https://example.com\"", "curl.exe -s -w '%{http_code}' https://example.com")]
    [InlineData("/bin/bash -lc 'git status --short'", "git status --short")]
    [InlineData("powershell.exe -Command 'axm validate'", "axm validate")]
    [InlineData("git diff", "git diff")]
    public void A_codex_command_is_what_its_shell_was_asked_to_run(string command, string unwrapped)
    {
        Assert.Equal(unwrapped, CodexSessions.Unwrap(command));
    }

    // Codex's exec stream doesn't show a spawn: the child's saved rollout names its agent and holds its tokens.
    [Fact]
    public void A_codex_custom_agent_is_a_load_read_from_its_child_s_rollout()
    {
        using var home = Rollouts("codex-agent-parent-rollout.jsonl", "codex-agent-child-rollout.jsonl");

        var session = CodexSessions.Read(Fixture("codex-agent.jsonl"), Copy, @"C:\Users\dev", Path.Combine(home.Root, "sessions"));

        Assert.Equal(["determinism-auditor"], session.Activity.Loads);
        Assert.Equal(new TokenCount(55_272 + 59_579, 40_448 + 43_392, 350 + 870), session.Tokens);
        Assert.Equal(2, session.ToolCalls);
    }

    // Codex's code mode answers a slow command with "Script running with cell ID 1", and the model waits on the
    // cell until it completes, so a command's time runs from its exec to the answer that completes its cell.
    [Fact]
    public void A_codex_command_s_time_runs_until_its_cell_completes()
    {
        using var home = Rollouts("codex-stalled-rollout.jsonl");
        string[] stream = ["{\"type\":\"thread.started\",\"thread_id\":\"id1\"}", "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1}}"];

        var session = CodexSessions.Read(stream, Copy, @"C:\Users\dev", Path.Combine(home.Root, "sessions"));

        var call = Assert.Single(session.Calls);
        Assert.Equal("git --version", call.What);
        Assert.Equal(141.4, call.Seconds, 1);
    }

    [Fact]
    public void A_code_mode_cell_is_named_by_the_commands_it_runs()
    {
        static string Line(string time, string payload) => $"{{\"timestamp\":\"2026-09-30T03:00:{time}Z\",\"type\":\"response_item\",\"payload\":{payload}}}";
        using var home = new TempVault().Write("sessions/2026/09/30/rollout-1.jsonl", string.Join('\n',
            "{\"timestamp\":\"2026-09-30T03:00:00.000Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\"id1\"}}",
            Line("01.000", """{"type":"custom_tool_call","name":"exec","call_id":"c1","input":"const r = await Promise.allSettled([tools.exec_command({cmd:\"axm list\"}), tools.exec_command({cmd:\"Get-Content \\\"a b.md\\\"\"})]);"}"""),
            Line("03.500", """{"type":"custom_tool_call_output","call_id":"c1","output":[{"type":"input_text","text":"Script completed\nWall time 2.5 seconds\nOutput:\n"}]}"""),
            Line("04.000", """{"type":"custom_tool_call","name":"exec","call_id":"c2","input":"text(await tools.apply_patch(\"*** Begin Patch\"));"}"""),
            Line("05.000", """{"type":"custom_tool_call_output","call_id":"c2","output":"Script completed\nWall time 1.0 seconds\nOutput:\n"}"""),
            Line("06.000", """{"type":"function_call","name":"exec_command","call_id":"c3","arguments":"{\"cmd\":\"git status\"}"}"""),
            Line("09.000", """{"type":"function_call_output","call_id":"c3","output":"On branch main"}""")) + "\n");
        string[] stream = ["{\"type\":\"thread.started\",\"thread_id\":\"id1\"}", "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1}}"];

        var session = CodexSessions.Read(stream, Copy, @"C:\Users\dev", Path.Combine(home.Root, "sessions"));

        Assert.Equal(
            [("axm list · Get-Content \"a b.md\"", 2.5), ("apply_patch", 1.0), ("git status", 3.0)],
            session.Calls.Select(call => (call.What, Math.Round(call.Seconds, 1))));
    }

    // A stopped Codex can still hold its rollout open for writing, as a real timeout showed.
    [Fact]
    public void A_rollout_codex_still_holds_open_is_read_all_the_same()
    {
        using var home = Rollouts("codex-agent-parent-rollout.jsonl", "codex-agent-child-rollout.jsonl");
        var child = Directory.EnumerateFiles(Path.Combine(home.Root, "sessions"), "*child*", SearchOption.AllDirectories).Single();
        using var writer = new FileStream(child, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var session = CodexSessions.Read(Fixture("codex-agent.jsonl"), Copy, @"C:\Users\dev", Path.Combine(home.Root, "sessions"));

        Assert.Equal(["determinism-auditor"], session.Activity.Loads);
    }

    [Fact]
    public void A_codex_session_that_fails_or_stops_early_says_why()
    {
        var lines = Fixture("codex-sandbox-refusals.jsonl");

        var failed = CodexSessions.Read([.. lines.SkipLast(1), "{\"type\":\"turn.failed\",\"error\":{\"message\":\"You've hit your usage limit.\"}}"], Copy, @"C:\Users\dev", null);
        var cut = CodexSessions.Read(lines.SkipLast(1), Copy, @"C:\Users\dev", null);

        Assert.Equal("Codex reported an error: You've hit your usage limit.", failed.Stopped);
        Assert.Equal(("it ended before its turn finished", (TokenCount?)null), (cut.Stopped, cut.Tokens));
        Assert.Equal(3, cut.Activity.Commands.Count);
    }
}

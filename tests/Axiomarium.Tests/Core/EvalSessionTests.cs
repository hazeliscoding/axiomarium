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
        Assert.Equal(
            ["axm list; Get-ChildItem hooks -Recurse | Select-Object FullName", "axm list", "axm list", "ls -a; axm --help 2>&1 | head -40", "axm validate", "axm list"],
            session.Activity.Commands);
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

using System.Collections.Concurrent;
using Axiomarium.Cli;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Tests.Cli;

public class TriggerSessionsTests
{
    private static TempVault Repo() => new TempVault()
        .Folder("repo/.git")
        .Folder("home/.codex")
        .Write("repo/CLAUDE.md", "Notes.\n")
        .Write("repo/src/app.ts", "export const a = 1;\n")
        .Write("repo/.claude/skills/release/SKILL.md", "---\ndescription: Releases the shop.\n---\nSteps.\n");

    private static TriggerSession Session(Harness harness, int prompt = 0, int run = 1) => new(harness, "release", prompt, run, $"Prompt {prompt}.");

    private static IReadOnlyList<SessionResult> Run(TempVault vault, IHarnessRunner runner, TriggerSession[] sessions, string? model = null)
    {
        var repo = Path.Combine(vault.Root, "repo");
        var plan = TriggerWorkspace.Plan(repo, null, TestMachine.For(vault.Root));
        return TriggerSessions.RunAsync(runner, plan, repo, sessions, model, home: @"C:\Users\dev").GetAwaiter().GetResult();
    }

    private static HarnessOutput Stream(HarnessCall call) =>
        FakeRunner.Fixture(call.Command == "claude" ? "claude-code-skill-then-bash.jsonl" : "codex-two-skills-read.jsonl");

    [Fact]
    public void Each_session_runs_in_the_copy_and_reports_what_it_loaded_then_the_copy_is_deleted()
    {
        using var vault = Repo();
        var copies = new ConcurrentBag<string>();
        var runner = new FakeRunner(call =>
        {
            copies.Add(string.Join(",", Directory.EnumerateFileSystemEntries(call.Folder!, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(call.Folder!, path).Replace('\\', '/')).Order(StringComparer.Ordinal)));
            return Stream(call);
        })
        { FolderRoot = vault.Root };

        var results = Run(vault, runner, [Session(Harness.ClaudeCode), Session(Harness.Codex)]);

        Assert.Equal(["release", "using-superpowers,db-migration"], results.Select(result => string.Join(",", result.Loads)));
        Assert.Equal(("claude-opus-5-5", "2.1.284", null), (results[0].Model, results[0].Version, results[0].Problem));
        Assert.All(copies, copy => Assert.Equal(".claude,.claude/skills,.claude/skills/release,.claude/skills/release/SKILL.md,.git,CLAUDE.md", copy));
        Assert.False(Directory.Exists(runner.Calls[0].Folder));
    }

    [Fact]
    public void Each_harness_gets_the_prompt_on_stdin_and_stops_at_its_first_other_action()
    {
        using var vault = Repo();
        var runner = new FakeRunner(Stream) { FolderRoot = vault.Root };

        Run(vault, runner, [Session(Harness.ClaudeCode), Session(Harness.Codex)], model: "haiku");

        var claude = runner.Calls.Single(call => call.Command == "claude");
        var codex = runner.Calls.Single(call => call.Command == "codex");
        Assert.Equal("-p --output-format stream-json --verbose --no-session-persistence --model haiku", string.Join(' ', claude.Arguments));
        Assert.Equal("exec --json -s read-only --ephemeral --skip-git-repo-check -m haiku -", string.Join(' ', codex.Arguments));
        Assert.Equal(("Prompt 0.", "Prompt 0."), (claude.Input, codex.Input));
        Assert.True(claude.StopAfter!("""{"type":"result","subtype":"success","is_error":false,"result":"."}"""));
        Assert.True(codex.StopAfter!("""{"type":"turn.completed"}"""));
    }

    [Fact]
    public void A_session_that_failed_or_never_acted_has_a_problem_and_no_loads()
    {
        using var vault = Repo();
        HarnessOutput[] outputs =
        [
            new(false, [], null, "claude isn't on PATH."),
            new(true, ["""{"type":"result","subtype":"error_during_execution","is_error":true,"result":"Invalid API key"}"""], 1, ""),
            new(true, ["""{"type":"turn.failed","error":{"message":"You've hit your usage limit."}}"""], 1, ""),
            new(true, ["""{"type":"system","subtype":"init","model":"claude-opus-5-5"}"""], null, ""),
        ];
        var runner = new FakeRunner(call => outputs[int.Parse(call.Input["Prompt ".Length..^1], System.Globalization.CultureInfo.InvariantCulture)]) { FolderRoot = vault.Root };

        var results = Run(vault, runner, [Session(Harness.ClaudeCode, 0), Session(Harness.ClaudeCode, 1), Session(Harness.Codex, 2), Session(Harness.ClaudeCode, 3)]);

        Assert.Equal(
            ["couldn't start: claude isn't on PATH.", "Invalid API key", "You've hit your usage limit.", "stopped after 3 minutes without acting"],
            results.OrderBy(result => result.Session.Prompt).Select(result => result.Problem));
        Assert.All(results, result => Assert.Empty(result.Loads));
    }

    [Fact]
    public void At_most_four_sessions_run_at_once()
    {
        using var vault = Repo();
        var running = 0;
        var most = 0;
        var runner = new SlowRunner(async call =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref most, now);
            await Task.Delay(30);
            Interlocked.Decrement(ref running);
            return Stream(call);
        })
        { FolderRoot = vault.Root };

        var results = Run(vault, runner, [.. Enumerable.Range(0, 12).Select(prompt => Session(Harness.ClaudeCode, prompt))]);

        Assert.Equal(12, results.Count);
        Assert.Equal(4, most);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }

    // Answers asynchronously, so sessions really overlap.
    private sealed class SlowRunner(Func<HarnessCall, Task<HarnessOutput>> respond) : IHarnessRunner
    {
        private int _folders;

        public string? FolderRoot { get; init; }

        public Task<HarnessOutput> RunAsync(HarnessCall call, CancellationToken cancellation = default) => respond(call);

        public string CreateFolder() => Directory.CreateDirectory(Path.Combine(FolderRoot!, $"slow-{Interlocked.Increment(ref _folders)}")).FullName;
    }
}

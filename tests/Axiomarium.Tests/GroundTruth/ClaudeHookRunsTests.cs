using System.Text.Json.Nodes;
using Axiomarium.GroundTruth;

namespace Axiomarium.Tests.GroundTruth;

public class ClaudeHookRunsTests
{
    private const string Recorder = "\"C:/tools/recorder\" hook-log \"C:/axm-ground-truth/basic/instructions-loaded\"";

    private static string Run(string type, string hookName, string command, string stdout) => new JsonObject
    {
        ["type"] = "attachment",
        ["attachment"] = new JsonObject
        {
            ["type"] = type,
            ["hookName"] = hookName,
            ["hookEvent"] = hookName.Split(':')[0],
            ["command"] = command,
            ["stdout"] = stdout,
            ["stderr"] = "",
            ["exitCode"] = 0,
        },
    }.ToJsonString();

    private static string Scenario(string hookName, string file, string label) =>
        Run("hook_success", hookName, $"echo MARKER {file} {label}", $"MARKER {file} {label}\n");

    // Hooks on one event run in parallel, so their order in the transcript changes from run to run.
    [Fact]
    public void Runs_are_named_by_their_marker_and_sorted_so_recordings_are_stable()
    {
        var transcript = new[]
        {
            Scenario("SessionStart:startup", "repo/.claude/settings.json", "project"),
            Scenario("SessionStart:startup", "home/.claude/settings.json", "user"),
            Scenario("PostToolUse:Edit", "repo/.claude/settings.json", "after-edit"),
            Scenario("PreToolUse:Edit", "repo/.claude/settings.json", "before-edit"),
        };

        var runs = ClaudeHookRuns.Parse(transcript, Recorder);

        Assert.Equal(
            [
                new HookRun("home/.claude/settings.json", "user", "SessionStart:startup"),
                new HookRun("repo/.claude/settings.json", "project", "SessionStart:startup"),
                new HookRun("repo/.claude/settings.json", "before-edit", "PreToolUse:Edit"),
                new HookRun("repo/.claude/settings.json", "after-edit", "PostToolUse:Edit"),
            ],
            runs);
    }

    [Fact]
    public void The_recorders_own_hook_is_left_out()
    {
        var transcript = new[] { Run("hook_success", "InstructionsLoaded", Recorder, "") };

        Assert.Empty(ClaudeHookRuns.Parse(transcript, Recorder));
    }

    [Fact]
    public void A_hook_that_failed_still_ran()
    {
        var transcript = new[] { Run("hook_non_blocking_error", "PostToolUse:Edit", "echo MARKER repo/.claude/settings.json late", "MARKER repo/.claude/settings.json late\n") };

        Assert.Equal([new HookRun("repo/.claude/settings.json", "late", "PostToolUse:Edit")], ClaudeHookRuns.Parse(transcript, Recorder));
    }

    // Only the event: the command may be someone's private hook.
    [Fact]
    public void A_hook_without_a_marker_is_an_error_that_leaves_out_its_command()
    {
        var transcript = new[] { Run("hook_success", "SessionStart:startup", "private-tool --secret", "hello\n") };

        var problem = Assert.Throws<GroundTruthException>(() => ClaudeHookRuns.Parse(transcript, Recorder));

        Assert.Contains("SessionStart:startup", problem.Message);
        Assert.DoesNotContain("private-tool", problem.Message);
    }
}

using Axiomarium.Core.Triggers;

namespace Axiomarium.Tests.Core;

public class ClaudeCodeStreamTests
{
    private static string[] Fixture(string name) => File.ReadAllLines(Path.Combine(RepoRoot.Path, "tests", "Axiomarium.Tests", "Fixtures", "triggers", name));

    [Fact]
    public void A_session_that_loads_a_skill_then_runs_a_command_reads_as_those_two_steps()
    {
        var session = ClaudeCodeStream.Read(Fixture("claude-code-skill-then-bash.jsonl"));

        Assert.Equal(("claude-opus-5-5", "2.1.284"), (session.Model, session.Version));
        Assert.Equal(["release", "code-review", "init"], session.Skills);
        Assert.Equal([new ClaudeCodeStep("Skill", "release", null), new ClaudeCodeStep("Bash", null, null)], session.Steps);
        Assert.Null(session.Result);
    }

    [Fact]
    public void A_text_answer_reads_as_one_text_step_and_the_result()
    {
        var session = ClaudeCodeStream.Read(Fixture("claude-code-text-answer.jsonl"));

        const string answer = """{"prompts": [{"prompt": "hello", "kind": "positive", "should_trigger": true}]}""";
        Assert.Equal([new ClaudeCodeStep(null, null, answer)], session.Steps);
        Assert.Equal((answer, false), (session.Result, session.IsError));
    }

    // Claude Code writes a warning line when stdin stays open, and a failed session ends in an error result.
    [Fact]
    public void Lines_that_are_not_events_are_skipped_and_an_error_result_says_so()
    {
        var session = ClaudeCodeStream.Read(
        [
            "Warning: no stdin data received in 3s, proceeding without it.",
            """{"type":"result","subtype":"error_during_execution","is_error":true,"result":"Invalid API key"}""",
        ]);

        Assert.Empty(session.Steps);
        Assert.Equal(("Invalid API key", true), (session.Result, session.IsError));
        Assert.Null(session.Model);
    }

    [Fact]
    public void The_first_tool_call_other_than_a_skill_ends_a_pick_and_nothing_else_does()
    {
        var lines = Fixture("claude-code-skill-then-bash.jsonl");

        Assert.Equal([false, false, false, false, false, true], lines.Select(ClaudeCodeStream.EndsPick));
        Assert.False(ClaudeCodeStream.EndsPick("not json"));
        Assert.True(ClaudeCodeStream.EndsPick("""{"type":"result","subtype":"success","is_error":false,"result":"Done."}"""));
    }
}

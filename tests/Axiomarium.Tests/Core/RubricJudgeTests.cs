using Axiomarium.Core.Judging;

namespace Axiomarium.Tests.Core;

public class RubricJudgeTests
{
    [Fact]
    public void The_brief_shows_the_task_the_rubric_and_what_the_session_did()
    {
        var brief = RubricJudge.Brief(
            "The hook warns and never blocks.",
            "Add a hook that warns when a migration changes.",
            "Added the hook.",
            ["axm validate", "git status"],
            "diff --git a/hooks/guard/asset.yaml b/hooks/guard/asset.yaml\n+response: warn\n");

        Assert.Contains("The user asked:\n\nAdd a hook that warns when a migration changes.\n", brief, StringComparison.Ordinal);
        Assert.Contains("The rubric:\n\nThe hook warns and never blocks.\n", brief, StringComparison.Ordinal);
        Assert.Contains("- axm validate\n- git status\n", brief, StringComparison.Ordinal);
        Assert.Contains("+response: warn\n", brief, StringComparison.Ordinal);
        Assert.Contains("Its final message:\n\nAdded the hook.\n", brief, StringComparison.Ordinal);
        Assert.Contains("""{"passed": true, "reason": "..."}""", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void A_session_that_did_nothing_says_so()
    {
        var brief = RubricJudge.Brief("Ask before deleting.", "Clean up.", null, [], "");

        Assert.Contains("It ran no commands.", brief, StringComparison.Ordinal);
        Assert.Contains("It changed no files.", brief, StringComparison.Ordinal);
        Assert.Contains("It ended without a final message.", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void The_answer_is_a_verdict_with_its_reason()
    {
        Assert.Equal(new RubricVerdict(false, "It blocks the edit."), RubricJudge.Read("""Verdict: {"passed": false, "reason": "It blocks the edit."}""").Verdict);
        Assert.Equal("the answer isn't JSON", RubricJudge.Read("It passes.").Problem);
        Assert.Equal("the answer has no passed true or false", RubricJudge.Read("""{"passed": "yes", "reason": "x"}""").Problem);
    }
}

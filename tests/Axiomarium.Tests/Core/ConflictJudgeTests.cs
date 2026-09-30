using Axiomarium.Core.Judging;

namespace Axiomarium.Tests.Core;

public class ConflictJudgeTests
{
    private static readonly JudgedFile[] Files =
    [
        new("CLAUDE.md", "# Style\n\nAlways indent with tabs.\nKeep functions short and\nfocused on one job.\n"),
        new("AGENTS.md", "# Rules\n\nIndent with two spaces, never tabs.\n"),
    ];

    [Fact]
    public void The_brief_shows_each_file_between_markers_and_asks_for_quotes_as_json()
    {
        var brief = ConflictJudge.Brief(Files);

        Assert.Contains("=== FILE: CLAUDE.md ===\n# Style\n\nAlways indent with tabs.\n", brief, StringComparison.Ordinal);
        Assert.Contains("=== FILE: AGENTS.md ===\n", brief, StringComparison.Ordinal);
        Assert.Contains("""{"contradictions": [{"first": {"file": "...", "quote": "..."}, "second": {"file": "...", "quote": "..."}, "why": "..."}]}""", brief, StringComparison.Ordinal);
        Assert.EndsWith("\n", brief, StringComparison.Ordinal);
    }

    // A quote the model reflowed still matches; one it made up, or put in the wrong file, doesn't.
    [Fact]
    public void Each_quote_must_be_in_its_file_and_gives_the_line_it_starts_on()
    {
        const string Answer = """
            Here's what I found:
            {"contradictions": [
              {"first": {"file": "CLAUDE.md", "quote": "Always indent with tabs."}, "second": {"file": "AGENTS.md", "quote": "Indent with two spaces, never tabs."}, "why": "Tabs or spaces."},
              {"first": {"file": "CLAUDE.md", "quote": "Keep functions short and focused on one job."}, "second": {"file": "AGENTS.md", "quote": "Write long functions."}, "why": "Made up."},
              {"first": {"file": "README.md", "quote": "Always indent with tabs."}, "second": {"file": "AGENTS.md", "quote": "never tabs"}, "why": "Wrong file."}
            ]}
            """;

        var read = ConflictJudge.Read(Answer, Files);

        Assert.Null(read.Problem);
        var found = Assert.Single(read.Found);
        Assert.Equal(
            (new Quote("CLAUDE.md", 3, "Always indent with tabs."), new Quote("AGENTS.md", 3, "Indent with two spaces, never tabs."), "Tabs or spaces."),
            (found.First, found.Second, found.Why));
        Assert.Equal(2, read.Dropped);
    }

    [Fact]
    public void A_quote_that_spans_a_line_break_starts_on_its_first_line()
    {
        var read = ConflictJudge.Read(
            """{"contradictions": [{"first": {"file": "CLAUDE.md", "quote": "short and focused"}, "second": {"file": "AGENTS.md", "quote": "never tabs"}, "why": "x"}]}""",
            Files);

        Assert.Equal(4, Assert.Single(read.Found).First.Line);
    }

    [Fact]
    public void No_contradictions_is_an_answer_and_no_json_is_a_problem()
    {
        Assert.Equal((0, 0, (string?)null), Summary(ConflictJudge.Read("""{"contradictions": []}""", Files)));
        Assert.Equal((0, 0, "the answer isn't JSON"), Summary(ConflictJudge.Read("I found none.", Files)));
        Assert.Equal((0, 0, "the answer has no contradictions list"), Summary(ConflictJudge.Read("""{"found": []}""", Files)));
    }

    private static (int, int, string?) Summary(ConflictAnswer answer) => (answer.Found.Count, answer.Dropped, answer.Problem);
}

using Axiomarium.Core.Evals;

namespace Axiomarium.Tests.Core;

public class EvalChecksTests
{
    private static readonly SessionActivity Session = new(
        Loads: ["using-superpowers", "agent-asset-authoring"],
        Commands:
        [
            "axm list; Get-ChildItem hooks",
            "\"C:\\Program Files\\axm\\axm.exe\" validate --root .",
            "git status --short && git diff",
        ],
        Reply: "Added the hook. axm validate reports 0 errors.");

    private static readonly Dictionary<string, RunOutcome> NoRuns = [];

    private static TempVault Copy() => new TempVault()
        .Write("hooks/migration-guard/asset.yaml", "name: migration-guard\nhook:\n  response: warn\n")
        .Write("hooks/migration-guard/hook.md", "# Migration guard\n")
        .Write(".git/config", "[core]\n");

    private static CheckResult Check(EvalCheck check, TempVault copy, Dictionary<string, RunOutcome>? runs = null) =>
        Assert.Single(EvalChecks.Evaluate([check], Session, copy.Root, runs ?? NoRuns));

    [Fact]
    public void A_file_check_passes_when_a_file_matches_its_glob_and_contains_its_text_in_any_case()
    {
        using var copy = Copy();

        Assert.Equal((true, "hooks/migration-guard/hook.md matches"), Result(Check(new(CheckKind.File, "hooks/*/hook.md"), copy)));
        Assert.Equal((true, "hooks/migration-guard/asset.yaml contains it"), Result(Check(new(CheckKind.File, "hooks/*/asset.yaml", Contains: "Response: WARN"), copy)));
        Assert.Equal((false, "no matching file contains it"), Result(Check(new(CheckKind.File, "hooks/*/asset.yaml", Contains: "block"), copy)));
        Assert.Equal((false, "no file matches"), Result(Check(new(CheckKind.File, "skills/*/skill.md"), copy)));
    }

    // The copy's .git is the harness's business, not the agent's work.
    [Fact]
    public void Not_turns_a_file_check_around_and_git_s_folder_never_matches()
    {
        using var copy = Copy();

        Assert.Equal((true, "no file matches"), Result(Check(new(CheckKind.File, ".git/**", Not: true), copy)));
        Assert.Equal((false, "hooks/migration-guard/hook.md matches"), Result(Check(new(CheckKind.File, "**/*.md", Not: true), copy)));
    }

    [Fact]
    public void A_run_check_compares_the_command_s_exit_code()
    {
        using var copy = Copy();
        var runs = new Dictionary<string, RunOutcome> { ["axm validate"] = new(0, "0 errors"), ["axm list --kind agent"] = new(2, "") };

        Assert.Equal((true, "exited 0"), Result(Check(new(CheckKind.Run, "axm validate"), copy, runs)));
        Assert.Equal((false, "exited 2"), Result(Check(new(CheckKind.Run, "axm list --kind agent"), copy, runs)));
        Assert.Equal((true, "exited 2"), Result(Check(new(CheckKind.Run, "axm list --kind agent", Exit: 2), copy, runs)));
        Assert.Throws<ArgumentException>(() => Check(new(CheckKind.Run, "make"), copy, runs));
    }

    [Fact]
    public void A_loaded_check_names_what_the_session_loaded_when_it_fails()
    {
        using var copy = Copy();

        Assert.Equal((true, "loaded"), Result(Check(new(CheckKind.Loaded, "agent-asset-authoring"), copy)));
        Assert.Equal((false, "not loaded; the session loaded using-superpowers and agent-asset-authoring"), Result(Check(new(CheckKind.Loaded, "systematic-debugging"), copy)));
        Assert.Equal((true, "not loaded; the session loaded using-superpowers and agent-asset-authoring"), Result(Check(new(CheckKind.Loaded, "systematic-debugging", Not: true), copy)));
        Assert.Equal(
            (false, "not loaded; the session loaded nothing"),
            Result(Assert.Single(EvalChecks.Evaluate([new(CheckKind.Loaded, "x")], Session with { Loads = [] }, copy.Root, NoRuns))));
    }

    // Agents chain commands and call binaries by path, so each step of a command counts, by its program's name.
    [Fact]
    public void A_ran_check_matches_any_step_of_any_command_by_its_leading_words()
    {
        using var copy = Copy();

        Assert.Equal((true, "ran axm list; Get-ChildItem hooks"), Result(Check(new(CheckKind.Ran, "Get-ChildItem"), copy)));
        Assert.Equal((true, "ran \"C:\\Program Files\\axm\\axm.exe\" validate --root ."), Result(Check(new(CheckKind.Ran, "axm validate"), copy)));
        Assert.Equal((true, "ran git status --short && git diff"), Result(Check(new(CheckKind.Ran, "git diff"), copy)));
        Assert.Equal((false, "not run"), Result(Check(new(CheckKind.Ran, "git push"), copy)));
        Assert.Equal((false, "not run"), Result(Check(new(CheckKind.Ran, "axm val"), copy)));
        Assert.Equal((true, "not run"), Result(Check(new(CheckKind.Ran, "git push", Not: true), copy)));
    }

    [Theory]
    [InlineData("& axm validate", true)]
    [InlineData("AXM.EXE Validate", true)]
    [InlineData("/usr/local/bin/axm validate", true)]
    [InlineData("'axm' validate", true)]
    [InlineData("axmx validate", false)]
    [InlineData("echo axm validate", false)]
    public void A_step_is_matched_by_its_program_s_name_whatever_its_path_quotes_or_case(string command, bool ran)
    {
        Assert.Equal(ran, EvalChecks.Ran(command, "axm validate"));
    }

    [Fact]
    public void A_reply_check_looks_for_its_text_in_the_final_message_in_any_case()
    {
        using var copy = Copy();

        Assert.Equal((true, "the reply contains it"), Result(Check(new(CheckKind.Reply, "AXM VALIDATE"), copy)));
        Assert.Equal((false, "the reply doesn't contain it"), Result(Check(new(CheckKind.Reply, "scope"), copy)));
        Assert.Equal(
            (true, "no reply"),
            Result(Assert.Single(EvalChecks.Evaluate([new(CheckKind.Reply, "scope", Not: true)], Session with { Reply = null }, copy.Root, NoRuns))));
    }

    [Fact]
    public void Each_check_says_what_it_expects()
    {
        Assert.Equal(
            [
                "hooks/*/hook.md exists",
                "no file matches .claude/**",
                "hooks/*/asset.yaml contains \"warn\"",
                "axm validate exits 0",
                "loaded agent-asset-authoring",
                "didn't load systematic-debugging",
                "ran axm validate",
                "didn't run git push",
                "the reply contains \"validate\"",
                "the reply doesn't contain \"sorry\"",
            ],
            new EvalCheck[]
            {
                new(CheckKind.File, "hooks/*/hook.md"),
                new(CheckKind.File, ".claude/**", Not: true),
                new(CheckKind.File, "hooks/*/asset.yaml", Contains: "warn"),
                new(CheckKind.Run, "axm validate"),
                new(CheckKind.Loaded, "agent-asset-authoring"),
                new(CheckKind.Loaded, "systematic-debugging", Not: true),
                new(CheckKind.Ran, "axm validate"),
                new(CheckKind.Ran, "git push", Not: true),
                new(CheckKind.Reply, "validate"),
                new(CheckKind.Reply, "sorry", Not: true),
            }.Select(EvalChecks.Describe));
    }

    private static (bool, string) Result(CheckResult result) => (result.Passed, result.Observed);
}

using Axiomarium.Core.Evals;
using Axiomarium.Core.Health;

namespace Axiomarium.Tests.Core;

public class EvalCasesTests
{
    private const string Case = "skills/x/evals/behavioral/new-hook/eval.yaml";

    private const string Valid = """
        prompt: Add a hook that warns when a migration file changes.
        allow:
          - axm validate
          - axm list
        checks:
          - file: hooks/*/hook.md
          - file: hooks/*/asset.yaml
            contains: "response: warn"
          - file: .claude/**
            not: true
          - run: axm validate
          - run: axm list --kind hook
            exit: 1
          - loaded: agent-asset-authoring
          - ran: git push
            not: true
          - reply: axm validate
        judge:
          rubric: The hook warns and never blocks.

        """;

    private static IReadOnlyList<Diagnostic> Doctor(string relativePath, string text)
    {
        using var vault = new TempVault().Asset("skills/x", TempVault.Manifest("skill", "x")).Write(relativePath, text);
        return Axiomarium.Core.Health.Doctor.Run(vault.Root).Report!.Diagnostics;
    }

    private static (int?, string)[] Problems(string text, EvalType type = EvalType.Behavioral) =>
        [.. EvalCases.Read(text, "new-hook", type).Problems.Select(problem => (problem.Location?.Line, problem.Message))];

    [Fact]
    public void A_valid_case_reads_as_typed_checks_and_the_doctor_is_quiet()
    {
        var read = EvalCases.Read(Valid, "new-hook", EvalType.Behavioral);

        Assert.Empty(read.Problems);
        var eval = read.Case!;
        Assert.Equal(("new-hook", EvalType.Behavioral, "Add a hook that warns when a migration file changes."), (eval.Name, eval.Type, eval.Prompt));
        Assert.Equal(["axm validate", "axm list"], eval.Allow);
        Assert.Equal(
            [
                new EvalCheck(CheckKind.File, "hooks/*/hook.md"),
                new EvalCheck(CheckKind.File, "hooks/*/asset.yaml", Contains: "response: warn"),
                new EvalCheck(CheckKind.File, ".claude/**", Not: true),
                new EvalCheck(CheckKind.Run, "axm validate"),
                new EvalCheck(CheckKind.Run, "axm list --kind hook", Exit: 1),
                new EvalCheck(CheckKind.Loaded, "agent-asset-authoring"),
                new EvalCheck(CheckKind.Ran, "git push", Not: true),
                new EvalCheck(CheckKind.Reply, "axm validate"),
            ],
            eval.Checks);
        Assert.Equal(("The hook warns and never blocks.", (string?)null), (eval.Rubric, eval.Guards));
        Assert.Empty(Doctor(Case, Valid));
    }

    [Fact]
    public void The_schema_is_checked_at_the_line_at_fault()
    {
        Assert.Equal(
            [(3, "Unknown field: checks[0].exits"), (1, "Missing required field: prompt")],
            Problems("checks:\n  - run: axm validate\n    exits: 0\n").OrderByDescending(problem => problem.Item1));
        Assert.Equal([(2, "checks needs at least 1 item")], Problems("prompt: Go.\nchecks: []\n"));
    }

    // A check that is two kinds at once, or takes a field of another kind, can't mean one thing.
    [Fact]
    public void Each_check_is_exactly_one_kind_and_takes_only_its_own_fields()
    {
        var problems = Problems("""
            prompt: Go.
            checks:
              - not: true
              - file: a.md
                run: make
              - loaded: x
                contains: y
              - reply: done
                exit: 0
              - run: make
                not: true

            """);

        Assert.Equal(
            [
                (3, "checks[0] needs one of file, run, loaded, ran or reply"),
                (4, "checks[1] has both file and run, and a check is one of them"),
                (7, "checks[2] has contains, which only a file check takes"),
                (9, "checks[3] has exit, which only a run check takes"),
                (11, "checks[4] is a run check, which says what to expect with exit, not with not"),
            ],
            problems);
    }

    [Fact]
    public void An_exit_code_must_fit_in_one()
    {
        Assert.Equal([(4, "checks[0].exit is too large for an exit code")], Problems("prompt: Go.\nchecks:\n  - run: make\n    exit: 99999999999\n"));
        Assert.Equal(-1073741819, EvalCases.Read("prompt: Go.\nchecks:\n  - run: make\n    exit: -1073741819\n", "c", EvalType.Behavioral).Case!.Checks[0].Exit);
    }

    [Fact]
    public void A_file_check_must_be_a_valid_glob_and_allow_holds_plain_commands()
    {
        Assert.Equal(
            [(4, "checks[0].file isn't a valid glob: '{' at column 7 is never closed.")],
            Problems("prompt: Go.\nallow: [axm validate]\nchecks:\n  - file: hooks/{a,b/hook.md\n"));
        var allow = Assert.Single(EvalCases.Read("prompt: Go.\nallow: [\"Bash(axm validate:*)\"]\nchecks:\n  - reply: ok\n", "c", EvalType.Behavioral).Problems);
        Assert.Equal(
            (2, "allow[0] \"Bash(axm validate:*)\" has the wrong format", "A command's leading words, such as axm validate, without parentheses or wildcards: axm writes each harness's own rule from them."),
            (allow.Location?.Line, allow.Message, Assert.Single(allow.Detail)));
    }

    // A regression eval exists because of a failure that happened, so it has to say which.
    [Fact]
    public void Only_a_regression_case_names_the_failure_it_guards_and_it_must()
    {
        const string Guarding = "prompt: Go.\nchecks:\n  - reply: ok\nguards: The agent pushed to main on 2026-10-02.\n";

        Assert.Equal([(4, "guards is only for regression cases")], Problems(Guarding));
        Assert.Empty(Problems(Guarding, EvalType.Regression));
        Assert.Equal(
            [(1, "A regression case needs guards: the failure it guards against")],
            Problems("prompt: Go.\nchecks:\n  - reply: ok\n", EvalType.Regression));
        Assert.Equal("The agent pushed to main on 2026-10-02.", EvalCases.Read(Guarding, "c", EvalType.Regression).Case!.Guards);
    }

    [Fact]
    public void The_doctor_reads_every_case_of_every_kind_of_asset()
    {
        using var vault = new TempVault()
            .Asset("hooks/h", TempVault.Manifest("hook", "h"))
            .Write("hooks/h/evals/regression/pushed-to-main/eval.yaml", "prompt: Go.\nchecks:\n  - reply: ok\n")
            .Write("hooks/h/evals/behavioral/warns/eval.yaml", Valid)
            .Write("hooks/h/evals/behavioral/warns/repo/src/app.ts", "export const a = 1;\n");

        var diagnostic = Assert.Single(Axiomarium.Core.Health.Doctor.Run(vault.Root).Report!.Diagnostics);

        Assert.Equal(
            ("hooks/h/evals/regression/pushed-to-main/eval.yaml", 1, "A regression case needs guards: the failure it guards against", Severity.Error),
            (diagnostic.File, diagnostic.Location?.Line, diagnostic.Message, diagnostic.Severity));
    }

    [Fact]
    public void Each_case_is_a_kebab_case_folder_with_its_eval_yaml()
    {
        var loose = Doctor("skills/x/evals/behavioral/notes.md", "# Notes\n");
        var missing = Doctor("skills/x/evals/behavioral/new-hook/eval.yml", Valid);
        var named = Doctor("skills/x/evals/behavioral/New_Hook/eval.yaml", Valid);

        Assert.Equal(
            ("skills/x/evals/behavioral/notes.md", "notes.md isn't in a case folder"),
            (Assert.Single(loose).File, loose[0].Message));
        Assert.Equal(("skills/x/evals/behavioral/new-hook", "Missing eval.yaml"), (Assert.Single(missing).File, missing[0].Message));
        Assert.Equal(["Rename eval.yml to eval.yaml."], missing[0].Detail);
        Assert.Equal(
            ("skills/x/evals/behavioral/New_Hook", "The case folder New_Hook isn't kebab-case"),
            (Assert.Single(named).File, named[0].Message));
    }

    [Fact]
    public void A_case_file_that_is_not_yaml_is_an_error()
    {
        var diagnostic = Assert.Single(Doctor(Case, "prompt: [unclosed\n"));

        Assert.Equal((Case, Severity.Error), (diagnostic.File, diagnostic.Severity));
    }
}

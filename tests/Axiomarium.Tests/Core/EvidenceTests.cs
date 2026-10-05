using Axiomarium.Core.Evidence;
using Axiomarium.Core.Health;
using Axiomarium.Core.Paths;

namespace Axiomarium.Tests.Core;

public class EvidenceTests
{
    private static EvidenceCheck Check(string name = "tests", string[]? run = null, params string[] covers) =>
        new(name, run ?? ["dotnet test"], [.. covers.Select(pattern => Glob.TryParse(pattern, out var glob, out _) ? glob : throw new ArgumentException(pattern))]);

    private static readonly DateTimeOffset Started = new(2026, 10, 4, 14, 2, 0, TimeSpan.Zero);

    private static EvidenceRecord Record(bool passed = true, int? exit = 0, IReadOnlyDictionary<string, string>? files = null, string[]? covers = null) => new(
        "tests", "dotnet test", passed, exit, Started, Started.AddSeconds(41), ".", new EvidenceSource("axm evidence record", null, null), null,
        covers ?? [], files ?? new Dictionary<string, string> { ["src/a.cs"] = "sha256:1", ["src/b.cs"] = "sha256:2" });

    [Fact]
    public void The_evidence_block_declares_each_check_with_its_commands_and_what_it_covers()
    {
        using var vault = new TempVault().Write("axiomarium.yaml", """
            evidence:
              checks:
                - name: tests
                  run: [dotnet test]
                  covers: [src/**, tests/**]
                - name: format
                  run: [dotnet format, dotnet csharpier]
            """);

        var (config, problems) = RepoConfig.Load(vault.Root);

        Assert.Empty(problems);
        Assert.Equal(
            [("tests", new[] { "dotnet test" }, new[] { "src/**", "tests/**" }), ("format", ["dotnet format", "dotnet csharpier"], [])],
            config.EvidenceChecks.Select(check => (check.Name, check.Run.ToArray(), check.Covers.Select(glob => glob.Pattern).ToArray())));
    }

    [Fact]
    public void A_check_named_twice_a_compound_command_or_an_invalid_glob_is_an_error_at_its_line()
    {
        using var vault = new TempVault();
        string[] Problems(string checks)
        {
            vault.Write("axiomarium.yaml", "evidence:\n  checks:\n" + checks);
            var (config, problems) = RepoConfig.Load(vault.Root);
            Assert.Empty(config.EvidenceChecks);
            return [.. problems.Select(problem => $"{problem.Location?.Line}: {problem.Message}")];
        }

        Assert.Equal(
            ["5: evidence.checks[1].name: tests is declared twice."],
            Problems("    - name: tests\n      run: [dotnet test]\n    - name: tests\n      run: [npm test]\n"));
        Assert.Single(Problems("    - name: tests\n      run: [dotnet build && dotnet test]\n"), problem => problem.StartsWith("4: ", StringComparison.Ordinal));
        Assert.Equal(
            ["6: evidence.checks[0].covers[0] isn't a valid pattern: '}' at column 4 has no '{' before it."],
            Problems("    - name: tests\n      run: [dotnet test]\n      covers:\n        - \"src}\"\n"));
    }

    [Theory]
    [InlineData("dotnet test", true)]
    [InlineData("dotnet test --no-build --filter \"Category=a;b\"", true)]
    [InlineData("  dotnet   test  ", true)]
    [InlineData("\"C:\\Program Files\\dotnet\\dotnet.exe\" test", true)]
    [InlineData("& dotnet test", true)]
    [InlineData("dotnet test 2>&1", true)]
    [InlineData("dotnet test > out.txt", true)]
    [InlineData("dotnet tests", false)]
    [InlineData("dotnet", false)]
    [InlineData("echo dotnet test", false)]
    public void A_command_counts_when_it_starts_with_a_run_entry_word_by_word(string command, bool counts) =>
        Assert.Equal(counts, EvidenceCommands.Counts(Check(), command));

    // One exit code can't vouch for one check when several commands share it, such as a pipe's last command.
    [Theory]
    [InlineData("dotnet build && dotnet test")]
    [InlineData("dotnet test || true")]
    [InlineData("dotnet test; echo done")]
    [InlineData("dotnet test | tail -5")]
    [InlineData("dotnet test &")]
    [InlineData("dotnet test\necho done")]
    public void A_compound_command_never_counts(string command) =>
        Assert.False(EvidenceCommands.Counts(Check(), command));

    [Fact]
    public void A_check_covers_every_file_git_sees_or_its_globs_but_never_the_repo_s_own_axm_folder()
    {
        string[] files = [".axm/evidence/tests.json", ".axm/scope", "README.md", "src/a.cs", "tests/a.cs", "evals/case/repo/.axm/scope"];

        Assert.Equal(["README.md", "src/a.cs", "tests/a.cs", "evals/case/repo/.axm/scope"], EvidenceFiles.Covered(Check(), files));
        Assert.Equal(["src/a.cs"], EvidenceFiles.Covered(Check("tests", null, "src/**", "docs/**"), files));
        Assert.Equal(["docs/**"], EvidenceFiles.Unmatched(Check("tests", null, "src/**", "docs/**"), files));
        Assert.Empty(EvidenceFiles.Unmatched(Check(), files));
    }

    [Fact]
    public void Each_covered_file_is_hashed_and_a_deleted_one_is_left_out()
    {
        using var vault = new TempVault().Write("src/a.cs", "a\n").Write("src/b.cs", "a\n");

        var hashes = EvidenceFiles.Hash(vault.Root, ["src/a.cs", "src/b.cs", "src/gone.cs"]);

        Assert.Equal(["src/a.cs", "src/b.cs"], hashes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("sha256:87428fc522803d31065e7bce3cf03fe475096631e5e07bbd7a0fde60c4cf25c7", hashes["src/a.cs"]);
        Assert.Equal(hashes["src/a.cs"], hashes["src/b.cs"]);
    }

    [Fact]
    public void A_check_is_missing_until_it_runs_and_fresh_while_its_files_are_unchanged()
    {
        var current = new Dictionary<string, string> { ["src/a.cs"] = "sha256:1", ["src/b.cs"] = "sha256:2" };

        var missing = EvidenceStatuses.Of(Check(), null, current);
        var fresh = EvidenceStatuses.Of(Check(), Record(), current);

        Assert.Equal((EvidenceState.Missing, "never run"), (missing.State, missing.Reason));
        Assert.Equal((EvidenceState.Fresh, (string?)null), (fresh.State, fresh.Reason));
    }

    [Fact]
    public void A_passed_check_goes_stale_when_a_covered_file_changes_appears_or_goes_and_names_up_to_three()
    {
        var current = new Dictionary<string, string> { ["src/a.cs"] = "sha256:9", ["src/c.cs"] = "sha256:3", ["src/d.cs"] = "sha256:4" };

        var stale = EvidenceStatuses.Of(Check(), Record(), current);

        Assert.Equal(EvidenceState.Stale, stale.State);
        Assert.Equal(["src/a.cs"], stale.Changed);
        Assert.Equal(["src/c.cs", "src/d.cs"], stale.Added);
        Assert.Equal(["src/b.cs"], stale.Deleted);
        Assert.Equal("src/a.cs changed, src/c.cs is new, src/d.cs is new, and 1 more file", stale.Reason);
        Assert.Equal("src/a.cs changed", EvidenceStatuses.Of(Check(), Record(), new Dictionary<string, string> { ["src/a.cs"] = "sha256:9", ["src/b.cs"] = "sha256:2" }).Reason);
    }

    // Freshness comes from content, not time: undoing an edit makes the check fresh again.
    [Fact]
    public void Undoing_an_edit_makes_a_check_fresh_again_and_changing_its_covers_makes_it_stale()
    {
        var undone = new Dictionary<string, string> { ["src/a.cs"] = "sha256:1", ["src/b.cs"] = "sha256:2" };

        Assert.Equal(EvidenceState.Fresh, EvidenceStatuses.Of(Check(), Record(), undone).State);
        var recovered = EvidenceStatuses.Of(Check("tests", null, "src/**"), Record(), undone);
        Assert.Equal((EvidenceState.Stale, "its covers changed"), (recovered.State, recovered.Reason));
    }

    [Fact]
    public void A_failed_run_is_failed_whatever_changed_since()
    {
        var current = new Dictionary<string, string> { ["src/a.cs"] = "sha256:1", ["src/b.cs"] = "sha256:2" };

        var failed = EvidenceStatuses.Of(Check(), Record(passed: false, exit: 128), current);
        var unknown = EvidenceStatuses.Of(Check(), Record(passed: false, exit: null), new Dictionary<string, string>());

        Assert.Equal((EvidenceState.Failed, "exit 128"), (failed.State, failed.Reason));
        Assert.Equal((EvidenceState.Failed, "it failed"), (unknown.State, unknown.Reason));
    }

    [Fact]
    public void A_record_survives_its_own_json_and_lives_in_the_repo_s_axm_evidence_folder()
    {
        using var vault = new TempVault();
        var record = Record() with
        {
            Folder = "src",
            RecordedBy = new EvidenceSource("axm hook evidence-freshness", "claude-code", "id1"),
            Head = new string('a', 40),
            Covers = ["src/**"],
            Files = new Dictionary<string, string> { ["src/a.cs"] = "sha256:" + new string('1', 64), ["src/b.cs"] = "sha256:" + new string('2', 64) },
        };

        vault.Write(".axm/evidence/tests.json", EvidenceRecords.ToJson(record));
        var (read, problem) = EvidenceRecords.Read(vault.Root, "tests");

        Assert.Null(problem);
        Assert.Equal(
            (record.Check, record.Command, record.Passed, record.Exit, record.Started, record.Ended, record.Folder, record.RecordedBy, record.Head),
            (read!.Check, read.Command, read.Passed, read.Exit, read.Started, read.Ended, read.Folder, read.RecordedBy, read.Head));
        Assert.Equal(record.Covers, read.Covers);
        Assert.Equal(record.Files.OrderBy(file => file.Key), read.Files.OrderBy(file => file.Key));
        Assert.Equal(Path.Combine(vault.Root, ".axm", "evidence", "tests.json"), EvidenceRecords.PathOf(vault.Root, "tests"));
    }

    [Fact]
    public void A_record_that_isn_t_one_says_why_and_a_missing_one_is_no_problem()
    {
        using var vault = new TempVault().Write(".axm/evidence/tests.json", """{ "schemaVersion": 1, "check": "tests" }""");

        var (record, problem) = EvidenceRecords.Read(vault.Root, "tests");
        var (none, noProblem) = EvidenceRecords.Read(vault.Root, "format");

        Assert.Null(record);
        Assert.StartsWith(".axm/evidence/tests.json isn't an evidence record: ", problem, StringComparison.Ordinal);
        Assert.Equal(((EvidenceRecord?)null, (string?)null), (none, noProblem));
        var status = EvidenceStatuses.Of(Check(), null, new Dictionary<string, string>(), problem);
        Assert.Equal((EvidenceState.Missing, problem), (status.State, status.Reason));
    }
}

using System.Text.Json.Nodes;
using Axiomarium.Cli;
using Axiomarium.Core.Evidence;

namespace Axiomarium.Tests.Cli;

// git and the recorded command go through a runner that answers as they would, so no test runs either for real.
public class EvidenceCommandTests
{
    private const string Config = """
        evidence:
          checks:
            - name: tests
              run: [dotnet test]
              covers: [src/**]
            - name: build
              run: [dotnet build]
            - name: format
              run: [dotnet format]

        """;

    private static readonly DateTimeOffset Noon = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static TempVault Repo() => new TempVault()
        .Write("axiomarium.yaml", Config)
        .Write("src/a.cs", "a\n")
        .Write("src/b.cs", "b\n")
        .Write("README.md", "# Repo\n");

    private static readonly string[] Files = ["README.md", "axiomarium.yaml", "src/a.cs", "src/b.cs"];

    // git answers as it does: the root with forward slashes, the folder below it with a trailing slash, NUL-ended paths,
    // and no HEAD in a repo with no commit. The tracked files come from tracked() when given, so a command can change them.
    private static FakeRunner Git(
        TempVault repo, string[]? files = null, int exit = 0, string? head = null, Action<HarnessCall>? onCommand = null, Func<string[]>? tracked = null, string[]? untracked = null) => new(call => call switch
    {
        { Command: "git", Arguments: ["rev-parse", "--show-toplevel"] } => new HarnessOutput(true, [repo.Root.Replace('\\', '/')], 0, ""),
        { Command: "git", Arguments: ["rev-parse", "--show-prefix"] } => new HarnessOutput(true, [Prefix(repo, call.Folder!)], 0, ""),
        { Command: "git", Arguments: ["ls-files", "-z", "--cached", "--recurse-submodules"] } => Listed(tracked?.Invoke() ?? files ?? Files),
        { Command: "git", Arguments: ["ls-files", "-z", "--others", "--exclude-standard"] } => Listed(untracked ?? [".axm/evidence/tests.json"]),
        { Command: "git", Arguments: ["rev-parse", "--verify", "--quiet", "HEAD"] } => head is null ? new HarnessOutput(true, [], 1, "") : new HarnessOutput(true, [head], 0, ""),
        { Command: "git" } => throw new InvalidOperationException($"Unexpected git call: {string.Join(' ', call.Arguments)}"),
        _ => Ran(call, exit, onCommand),
    });

    private static string Prefix(TempVault repo, string folder) =>
        Path.GetRelativePath(repo.Root, folder) is "." ? "" : Path.GetRelativePath(repo.Root, folder).Replace('\\', '/') + "/";

    private static HarnessOutput Listed(string[] paths) => new(true, paths.Length == 0 ? [] : [string.Join('\0', paths) + "\0"], 0, "");

    private static HarnessOutput Ran(HarnessCall call, int exit, Action<HarnessCall>? onCommand)
    {
        onCommand?.Invoke(call);
        return new HarnessOutput(true, [], exit, "");
    }

    // A record of a passing run that ended at the given time, with the files' hashes as they are now, unless some are given.
    private static void Recorded(TempVault repo, string check, string command, DateTimeOffset ended, string[] covers, bool passed = true, params (string Path, string Hash)[] hashes)
    {
        var current = EvidenceFiles.Hash(repo.Root, check == "tests" ? ["src/a.cs", "src/b.cs"] : ["README.md", "axiomarium.yaml", "src/a.cs", "src/b.cs"]);
        var files = current.ToDictionary(file => file.Key, file => hashes.FirstOrDefault(given => given.Path == file.Key).Hash ?? file.Value);
        repo.Write($".axm/evidence/{check}.json", EvidenceRecords.ToJson(new EvidenceRecord(
            check, command, passed, passed ? 0 : 1, ended.AddSeconds(-30), ended, ".", new EvidenceSource("axm evidence record", null, null), null, covers, files)));
    }

    private static (int ExitCode, string Output, string Error) Run(TempVault repo, FakeRunner runner, params string[] args) =>
        CliRun.Run(["evidence", .. args], currentDirectory: repo.Root, runner: runner, clock: new FixedClock());

    [Fact]
    public void Each_declared_check_shows_its_status_when_it_ran_and_why()
    {
        using var repo = Repo();
        Recorded(repo, "tests", "dotnet test", Noon.AddMinutes(-1), ["src/**"], hashes: ("src/a.cs", "sha256:" + new string('0', 64)));
        Recorded(repo, "build", "dotnet build", Noon.AddMinutes(-2), []);

        var (exitCode, output, error) = Run(repo, Git(repo));

        Assert.Equal((0, ""), (exitCode, error));
        Assert.Equal(
            """
            AXM EVIDENCE // 3 checks

              01  tests   STALE    passed 2026-09-28 11:59 · dotnet test
                                   src/a.cs changed
              02  build   FRESH    passed 2026-09-28 11:58 · dotnet build
              03  format  MISSING  never run

            3 checks · 1 fresh · 1 stale · 1 missing

            """,
            output);
    }

    // A run below the repo root may test only part of it, and a failed run says how it ended.
    [Fact]
    public void A_narrowed_run_shows_its_folder_and_a_failed_one_its_exit_code()
    {
        using var repo = Repo();
        repo.Write(".axm/evidence/tests.json", EvidenceRecords.ToJson(new EvidenceRecord(
            "tests", "dotnet test", false, 1, Noon.AddMinutes(-2), Noon.AddMinutes(-1), "src/api", new EvidenceSource("axm evidence record", null, null), null, ["src/**"],
            EvidenceFiles.Hash(repo.Root, ["src/a.cs", "src/b.cs"]))));

        var (exitCode, output, _) = Run(repo, Git(repo), "check", "tests");

        Assert.Equal(1, exitCode);
        Assert.Contains("  01  tests  FAILED  failed 2026-09-28 11:59 · dotnet test in src/api\n                     exit 1\n", output, StringComparison.Ordinal);
    }

    // The gate fails on a stale check as on a failed or missing one.
    [Fact]
    public void Check_fails_on_a_stale_check_and_shows_why()
    {
        using var repo = Repo();
        Recorded(repo, "tests", "dotnet test", Noon.AddMinutes(-1), ["src/**"], hashes: ("src/a.cs", "sha256:" + new string('0', 64)));

        var (exitCode, output, _) = Run(repo, Git(repo), "check", "tests");

        Assert.Equal((1, "AXM EVIDENCE CHECK // 1 check\n\n  01  tests  STALE  passed 2026-09-28 11:59 · dotnet test\n                    src/a.cs changed\n\n1 check · 1 stale\n"), (exitCode, output));
    }

    [Fact]
    public void Json_holds_each_check_s_state_reason_latest_run_and_what_changed()
    {
        using var repo = Repo();
        Recorded(repo, "tests", "dotnet test", Noon.AddMinutes(-1), ["src/**"], hashes: ("src/a.cs", "sha256:" + new string('0', 64)));

        var (exitCode, output, _) = Run(repo, Git(repo), "--json");

        Assert.Equal(0, exitCode);
        var json = JsonNode.Parse(output)!;
        Assert.Equal((1, "evidence"), (json["schemaVersion"]!.GetValue<int>(), json["command"]!.GetValue<string>()));
        var tests = json["checks"]![0]!;
        Assert.Equal(("tests", "stale", "src/a.cs changed", false), (tests["name"]!.GetValue<string>(), tests["state"]!.GetValue<string>(), tests["reason"]!.GetValue<string>(), tests["coversChanged"]!.GetValue<bool>()));
        Assert.Equal(["src/a.cs"], tests["changed"]!.AsArray().Select(path => path!.GetValue<string>()));
        Assert.Equal(("dotnet test", true, 0, "2026-09-28T11:59:00Z", "."), (tests["run"]!["command"]!.GetValue<string>(), tests["run"]!["passed"]!.GetValue<bool>(), tests["run"]!["exit"]!.GetValue<int>(), tests["run"]!["ended"]!.GetValue<string>(), tests["run"]!["folder"]!.GetValue<string>()));
        Assert.Null(json["checks"]![2]!["run"]);
        Assert.Equal("never run", json["checks"]![2]!["reason"]!.GetValue<string>());
    }

    [Fact]
    public void Outside_a_git_repo_without_checks_or_with_a_glob_that_matches_nothing_it_can_t_run()
    {
        using var repo = Repo();
        var notGit = new FakeRunner(_ => new HarnessOutput(true, [], 128, "fatal: not a git repository"));
        using var bare = new TempVault();
        using var nothing = Repo().Write("axiomarium.yaml", "evidence:\n  checks:\n    - name: tests\n      run: [dotnet test]\n      covers: [docs/**]\n");

        Assert.Equal(
            (2, "axm: axm evidence needs a git repo, and the current folder isn't in one.\n     It compares the files git sees. Run it inside a git repo.\n"),
            (Run(repo, notGit).ExitCode, Run(repo, notGit).Error));
        Assert.Equal(
            (2, "axm: axiomarium.yaml declares no evidence checks.\n     Add evidence.checks to axiomarium.yaml, such as a check named tests with run: [dotnet test].\n"),
            (Run(bare, Git(bare)).ExitCode, Run(bare, Git(bare)).Error));
        Assert.Equal(
            (2, "axm: The check tests covers docs/**, which matches no file git sees, so it would vouch for nothing.\n     Fix the glob in axiomarium.yaml, or drop it.\n"),
            (Run(nothing, Git(nothing)).ExitCode, Run(nothing, Git(nothing)).Error));
    }

    // git refuses a repo of dubious ownership with its own fix, which an "isn't in a repo" message would hide.
    [Fact]
    public void A_repo_git_refuses_shows_git_s_reason()
    {
        using var repo = Repo();
        var refused = new FakeRunner(_ => new HarnessOutput(true, [], 128, "fatal: detected dubious ownership in repository at 'C:/repo'\nTo add an exception for this directory, call:\n\n\tgit config --global --add safe.directory C:/repo\n"));

        var (exitCode, _, error) = Run(repo, refused);

        Assert.Equal(2, exitCode);
        Assert.StartsWith("axm: git couldn't find the repo: fatal: detected dubious ownership", error, StringComparison.Ordinal);
        Assert.Contains("git config --global --add safe.directory C:/repo", error, StringComparison.Ordinal);
    }

    // A submodule that isn't checked out is listed as a folder, which holds no file to hash, so it covers nothing.
    [Fact]
    public void A_glob_that_reaches_only_a_submodule_folder_matches_nothing()
    {
        using var repo = Repo().Folder("vendor/lib").Write("axiomarium.yaml", "evidence:\n  checks:\n    - name: tests\n      run: [dotnet test]\n      covers: [vendor/**]\n");

        var (exitCode, _, error) = Run(repo, Git(repo, files: [.. Files, "vendor/lib"]));

        Assert.Equal((2, "axm: The check tests covers vendor/**, which matches no file git sees, so it would vouch for nothing.\n     Fix the glob in axiomarium.yaml, or drop it.\n"), (exitCode, error));
    }

    [Fact]
    public void An_invalid_evidence_block_can_t_run_and_says_where_and_what_it_needs()
    {
        using var repo = Repo().Write("axiomarium.yaml", "evidence:\n  checks:\n    - name: tests\n      run: [dotnet test]\n    - name: tests\n      run: [npm test]\n");
        using var named = Repo().Write("axiomarium.yaml", "evidence:\n  checks:\n    - name: Tests\n      run: [dotnet test]\n");

        var (exitCode, _, error) = Run(repo, Git(repo));
        var (_, _, detail) = Run(named, Git(named));

        Assert.Equal(2, exitCode);
        Assert.Equal("axm: axiomarium.yaml:5 evidence.checks[1].name: tests is declared twice.\n     Fix axiomarium.yaml, then run axm evidence again.\n", error);
        Assert.StartsWith("axm: axiomarium.yaml:3 evidence.checks[0].name \"Tests\" has the wrong format", detail, StringComparison.Ordinal);
        Assert.Contains("\n     The check's kebab-case name, unique in the repo", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_prints_only_the_checks_that_aren_t_fresh_and_fails_when_one_isn_t()
    {
        using var repo = Repo();
        Recorded(repo, "tests", "dotnet test", Noon.AddMinutes(-1), ["src/**"]);
        Recorded(repo, "build", "dotnet build", Noon.AddMinutes(-2), []);

        var all = Run(repo, Git(repo), "check");
        var named = Run(repo, Git(repo), "check", "tests", "build");
        var unknown = Run(repo, Git(repo), "check", "lint");

        Assert.Equal((1, "AXM EVIDENCE CHECK // 3 checks\n\n  03  format  MISSING  never run\n\n3 checks · 2 fresh · 1 missing\n"), (all.ExitCode, all.Output));
        Assert.Equal((0, "AXM EVIDENCE CHECK // 2 checks\n\n2 checks · 2 fresh\n"), (named.ExitCode, named.Output));
        Assert.Equal((2, "axm: axiomarium.yaml declares no check named lint.\n     Its checks: tests, build, format.\n"), (unknown.ExitCode, unknown.Error));
    }

    [Fact]
    public void Record_runs_the_check_s_first_command_in_the_current_folder_and_keeps_what_it_covered()
    {
        using var repo = Repo();
        HarnessCall? ran = null;
        var runner = Git(repo, head: new string('a', 40), onCommand: call => ran = call);

        var (exitCode, output, error) = Run(repo, runner, "record", "tests");

        Assert.Equal((0, ""), (exitCode, error));
        Assert.Equal(["test"], ran!.Arguments);
        Assert.Equal(("dotnet", repo.Root, true), (ran.Command, ran.Folder, ran.Attached));
        Assert.Equal("AXM EVIDENCE RECORD // tests passed · dotnet test\n\nrecorded in .axm/evidence/tests.json\n", output);
        var (record, problem) = EvidenceRecords.Read(repo.Root, "tests");
        Assert.Null(problem);
        Assert.Equal(("dotnet test", true, 0L, ".", "axm evidence record", new string('a', 40), Noon, Noon), (record!.Command, record.Passed, record.Exit, record.Folder, record.RecordedBy.Tool, record.Head, record.Started, record.Ended));
        Assert.Equal(["src/**"], record.Covers);
        Assert.Equal(EvidenceFiles.Hash(repo.Root, ["src/a.cs", "src/b.cs"]).OrderBy(file => file.Key), record.Files.OrderBy(file => file.Key));
    }

    [Fact]
    public void Record_runs_the_given_command_records_a_failure_and_fails_with_it()
    {
        using var repo = Repo().Folder("src/api");
        HarnessCall? ran = null;

        var (exitCode, output, _) = CliRun.Run(
            ["evidence", "record", "tests", "--", "dotnet", "test", "--filter", "Name~a b|Category=Fast"], currentDirectory: Path.Combine(repo.Root, "src", "api"),
            runner: Git(repo, exit: 3, onCommand: call => ran = call), clock: new FixedClock());

        Assert.Equal(1, exitCode);
        Assert.Equal(("dotnet", ["test", "--filter", "Name~a b|Category=Fast"]), (ran!.Command, ran.Arguments.ToArray()), new WordsComparer());
        Assert.Equal("AXM EVIDENCE RECORD // tests failed · dotnet test --filter 'Name~a b|Category=Fast' in src/api\n\nexit 3 · recorded in .axm/evidence/tests.json\n", output);
        var record = EvidenceRecords.Read(repo.Root, "tests").Record!;
        Assert.Equal((false, 3L, "src/api", (string?)null), (record.Passed, record.Exit, record.Folder, record.Head));
        Assert.Equal(EvidenceState.Failed, EvidenceStatuses.InRepo(repo.Root, new EvidenceCheck("tests", ["dotnet test"], []), Files).State);
    }

    // The command may write a covered file, such as a snapshot, or a new one, so the record holds the files as it left them.
    [Fact]
    public void Record_lists_and_hashes_the_files_after_the_command_ends()
    {
        using var repo = Repo();
        var files = Files.ToList();
        var runner = Git(repo, tracked: () => [.. files], onCommand: _ =>
        {
            repo.Write("src/a.cs", "changed by the run\n").Write("src/c.cs", "new\n");
            files.Add("src/c.cs");
        });

        Run(repo, runner, "record", "tests");
        var record = EvidenceRecords.Read(repo.Root, "tests").Record!;

        Assert.Equal(EvidenceFiles.Hash(repo.Root, ["src/a.cs", "src/b.cs", "src/c.cs"]).OrderBy(file => file.Key), record.Files.OrderBy(file => file.Key));
        Assert.Equal(0, Run(repo, runner, "check", "tests").ExitCode);
    }

    // With no shell to fail in its place, a program that isn't there can't start, and the last record stands.
    [Fact]
    public void Record_of_a_program_that_can_t_start_writes_nothing_and_can_t_run()
    {
        using var repo = Repo();
        Recorded(repo, "tests", "dotnet test", Noon.AddMinutes(-1), ["src/**"]);
        var before = File.ReadAllText(EvidenceRecords.PathOf(repo.Root, "tests"));
        var missing = new FakeRunner(call => call.Command == "git" ? Git(repo).RunAsync(call).GetAwaiter().GetResult() : new HarnessOutput(false, [], null, "dotnet isn't on PATH."));

        var (exitCode, _, error) = Run(repo, missing, "record", "tests");

        Assert.Equal((2, "axm: dotnet isn't on PATH.\n     Nothing was recorded.\n"), (exitCode, error));
        Assert.Equal(before, File.ReadAllText(EvidenceRecords.PathOf(repo.Root, "tests")));
    }

    private sealed class WordsComparer : IEqualityComparer<(string, string[])>
    {
        public bool Equals((string, string[]) x, (string, string[]) y) => x.Item1 == y.Item1 && x.Item2.SequenceEqual(y.Item2);

        public int GetHashCode((string, string[]) obj) => obj.Item1.GetHashCode(StringComparison.Ordinal);
    }

    [Fact]
    public void Record_refuses_a_command_that_doesn_t_count_and_an_unknown_check_before_running_anything()
    {
        using var repo = Repo();
        var ran = false;
        var runner = Git(repo, onCommand: _ => ran = true);

        var wrong = Run(repo, runner, "record", "tests", "--", "echo", "ok");
        var unknown = Run(repo, runner, "record", "lint");

        Assert.Equal((2, "axm: echo ok doesn't count as running tests.\n     A command counts when it starts with one of the check's run entries: dotnet test.\n"), (wrong.ExitCode, wrong.Error));
        Assert.Equal(2, unknown.ExitCode);
        Assert.False(ran);
        Assert.False(File.Exists(EvidenceRecords.PathOf(repo.Root, "tests")));
    }
}

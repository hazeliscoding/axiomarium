using System.CommandLine;
using System.Runtime.InteropServices;
using Axiomarium.Core.Evidence;
using Axiomarium.Core.Health;

namespace Axiomarium.Cli;

public static partial class AxmCli
{
    // git answers in a moment. A recorded command, such as a test run, takes as long as it takes.
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(2);

    private const string RecordTool = "axm evidence record";

    private static Command EvidenceCommand(Session session)
    {
        var json = new Option<bool>("--json") { Description = "Print JSON. Its shape is a contract, versioned by schemaVersion." };
        var command = new Command("evidence", "Show whether each check in axiomarium.yaml still vouches for the files as they are now: FRESH, STALE, FAILED or MISSING.");
        command.Options.Add(json);
        command.Subcommands.Add(EvidenceCheckCommand(session));
        command.Subcommands.Add(EvidenceRecordCommand(session));
        command.SetAction(result =>
        {
            if (OpenEvidence(session) is not { } repo)
            {
                return CouldNotRun;
            }

            var statuses = repo.Checks.Select(check => EvidenceStatuses.InRepo(repo.Root, check, repo.Files)).ToList();
            if (result.GetValue(json))
            {
                EvidenceJson.Write(session.Output, statuses);
            }
            else
            {
                EvidenceText.Write(session.Output, "AXM EVIDENCE", repo.Checks, statuses, session.Clock.LocalTimeZone, session.OutputStyle);
            }

            // Stale or missing evidence is what this command shows, not an error it found. axm evidence check gates.
            return Passed;
        });
        return command;
    }

    private static Command EvidenceCheckCommand(Session session)
    {
        var names = new Argument<string[]>("name") { Description = "Only these checks. Defaults to every check.", Arity = ArgumentArity.ZeroOrMore };
        var command = new Command("check", "Print only the checks that aren't FRESH, and fail when one isn't: a gate for a pre-commit hook.");
        command.Arguments.Add(names);
        command.SetAction(result =>
        {
            if (OpenEvidence(session) is not { } repo)
            {
                return CouldNotRun;
            }

            var wanted = result.GetValue(names) ?? [];
            if (wanted.FirstOrDefault(name => repo.Checks.All(check => check.Name != name)) is { } unknown)
            {
                return UnknownCheck(session, repo, unknown);
            }

            var checks = wanted.Length == 0 ? repo.Checks : [.. repo.Checks.Where(check => wanted.Contains(check.Name))];
            var statuses = checks.Select(check => EvidenceStatuses.InRepo(repo.Root, check, repo.Files)).ToList();
            EvidenceText.WriteCheck(session.Output, repo.Checks, statuses, session.Clock.LocalTimeZone, session.OutputStyle);
            return statuses.All(status => status.State == EvidenceState.Fresh) ? Passed : ErrorsFound;
        });
        return command;
    }

    private static Command EvidenceRecordCommand(Session session)
    {
        var name = new Argument<string>("name") { Description = "The check, by its name in axiomarium.yaml." };
        var words = new Argument<string[]>("command") { Description = "The command to run, after --. Defaults to the check's first run entry.", Arity = ArgumentArity.ZeroOrMore };
        var command = new Command("record", "Run a check's command with its output passed through, then record how it ended and what the files it covers hold.");
        command.Arguments.Add(name);
        command.Arguments.Add(words);
        command.SetAction(result =>
        {
            if (OpenEvidence(session) is not { } repo)
            {
                return CouldNotRun;
            }

            if (repo.Checks.FirstOrDefault(declared => declared.Name == result.GetValue(name)) is not { } check)
            {
                return UnknownCheck(session, repo, result.GetValue(name)!);
            }

            var given = result.GetValue(words) ?? [];
            var line = given.Length == 0 ? check.Run[0] : string.Join(' ', given.Select(Quoted));
            if (!EvidenceCommands.Counts(check, line))
            {
                return CouldNotRunWith(
                    session, $"{line} doesn't count as running {check.Name}.", $"A command counts when it starts with one of the check's run entries: {string.Join(", ", check.Run)}.");
            }

            // The command runs as the user would run it, in a shell, with its output on the user's terminal.
            var shell = session.Platform == OSPlatform.Windows ? new HarnessCall("cmd", ["/d", "/s", "/c", line], "", session.CurrentDirectory, null, Timeout.InfiniteTimeSpan)
                : new HarnessCall("sh", ["-c", line], "", session.CurrentDirectory, null, Timeout.InfiniteTimeSpan);
            var started = session.Clock.GetUtcNow();
            var ran = session.Runner.RunAsync(shell with { Attached = true }).GetAwaiter().GetResult();
            var ended = session.Clock.GetUtcNow();
            if (!ran.Started)
            {
                return CouldNotRunWith(session, $"{line} couldn't start: {ran.Error}", hint: null);
            }

            // The command may have made or removed files, so git lists them again, and the hashes are of the files as it left them.
            if (ListFiles(session, repo.Root) is not { } files)
            {
                return CouldNotRun;
            }

            var head = Git(session, repo.Root, "rev-parse", "--verify", "--quiet", "HEAD");
            var folder = Path.GetRelativePath(repo.Root, session.CurrentDirectory).Replace('\\', '/');
            var record = new EvidenceRecord(
                check.Name,
                line,
                ran.ExitCode == 0,
                ran.ExitCode,
                started,
                ended,
                folder,
                new EvidenceSource(RecordTool, null, null),
                head.ExitCode == 0 && head.Lines.Count > 0 ? head.Lines[0].Trim() : null,
                [.. check.Covers.Select(glob => glob.Pattern)],
                EvidenceFiles.Hash(repo.Root, EvidenceFiles.Covered(check, files)));
            EvidenceStore.Save(repo.Root, record);
            EvidenceText.WriteRecorded(session.Output, record, session.OutputStyle);
            return record.Passed ? Passed : ErrorsFound;
        });
        return command;
    }

    private static int UnknownCheck(Session session, EvidenceRepo repo, string name) =>
        CouldNotRunWith(session, $"axiomarium.yaml declares no check named {name}.", $"Its checks: {string.Join(", ", repo.Checks.Select(check => check.Name))}.");

    // A word the shell split off goes back in quotes when it holds a space, so the command runs as typed.
    private static string Quoted(string word) =>
        word.Length > 0 && !word.Any(char.IsWhiteSpace) ? word : $"\"{word.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    // The repo around the current folder, its checks and the files git sees, or null after saying why it can't be used.
    private static EvidenceRepo? OpenEvidence(Session session)
    {
        var top = Git(session, session.CurrentDirectory, "rev-parse", "--show-toplevel");
        if (!top.Started)
        {
            CouldNotRunWith(session, "axm evidence needs git, which isn't on PATH.", hint: null);
            return null;
        }

        if (top.ExitCode != 0 || top.Lines.Count == 0)
        {
            CouldNotRunWith(session, "axm evidence needs a git repo, and the current folder isn't in one.", "It compares the files git sees. Run it inside a git repo.");
            return null;
        }

        var root = Path.GetFullPath(top.Lines[0].Trim());
        var (config, problems) = RepoConfig.Load(root);
        if (problems.Count > 0)
        {
            WriteCouldNotRun(
                session.Error,
                session.ErrorStyle,
                [.. problems.Select(problem => $"{problem.File}{(problem.Location is { } at ? $":{at.Line}" : "")} {problem.Message}")],
                "Fix axiomarium.yaml, then run axm evidence again.");
            return null;
        }

        if (config.EvidenceChecks.Count == 0)
        {
            CouldNotRunWith(session, "axiomarium.yaml declares no evidence checks.", "Add evidence.checks to axiomarium.yaml, such as a check named tests with run: [dotnet test].");
            return null;
        }

        if (ListFiles(session, root) is not { } files)
        {
            return null;
        }

        // A glob that matches nothing would leave its check fresh whatever changed.
        var unmatched = config.EvidenceChecks
            .SelectMany(check => EvidenceFiles.Unmatched(check, files).Select(pattern => $"The check {check.Name} covers {pattern}, which matches no file git sees, so it would vouch for nothing."))
            .ToList();
        if (unmatched.Count > 0)
        {
            WriteCouldNotRun(session.Error, session.ErrorStyle, unmatched, "Fix the glob in axiomarium.yaml, or drop it.");
            return null;
        }

        return new EvidenceRepo(root, config.EvidenceChecks, files);
    }

    // Tracked files, and untracked ones git doesn't ignore, by their paths with forward slashes, or null after saying why not.
    private static IReadOnlyList<string>? ListFiles(Session session, string root)
    {
        var listed = Git(session, root, "ls-files", "-z", "--cached", "--others", "--exclude-standard");
        if (!listed.Started || listed.ExitCode != 0)
        {
            CouldNotRunWith(session, $"git couldn't list the repo's files: {listed.Error.Trim()}", hint: null);
            return null;
        }

        // -z ends each path with a NUL, so a path with a line break in it arrives whole once the lines are joined again.
        return [.. string.Join('\n', listed.Lines).Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static HarnessOutput Git(Session session, string folder, params string[] arguments) =>
        session.Runner.RunAsync(new HarnessCall("git", arguments, "", folder, null, GitTimeout)).GetAwaiter().GetResult();

    // A repo whose checks axm evidence can decide: its root, the checks it declares, and the files git sees.
    private sealed record EvidenceRepo(string Root, IReadOnlyList<EvidenceCheck> Checks, IReadOnlyList<string> Files);
}

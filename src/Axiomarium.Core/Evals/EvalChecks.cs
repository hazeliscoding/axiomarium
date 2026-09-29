using System.Text;
using System.Text.RegularExpressions;
using Axiomarium.Core.Paths;

namespace Axiomarium.Core.Evals;

/// <summary>What a session did, as its harness recorded it, never as its reply claims.</summary>
/// <param name="Loads">The skills it loaded and the agents it ran, by name, in the order it did so.</param>
/// <param name="Commands">The shell commands it ran, as it wrote them, in order.</param>
/// <param name="Reply">Its final message, or <see langword="null"/> when it ended without one.</param>
public sealed record SessionActivity(IReadOnlyList<string> Loads, IReadOnlyList<string> Commands, string? Reply);

/// <summary>How a <see cref="CheckKind.Run"/> check's command ended when it ran in the copy after the session.</summary>
/// <param name="ExitCode">Its exit code.</param>
/// <param name="Output">What it printed, standard output then standard error.</param>
public sealed record RunOutcome(int ExitCode, string Output);

/// <summary>How one check of one run turned out.</summary>
/// <param name="Check">The check.</param>
/// <param name="Passed">Whether it passed, with <see cref="EvalCheck.Not"/> taken into account.</param>
/// <param name="Observed">What the check found, as a short phrase such as <c>exited 2</c> or <c>no file matches</c>.</param>
public sealed record CheckResult(EvalCheck Check, bool Passed, string Observed);

/// <summary>Decides an eval case's checks for one run, from what the session did and what it left in the copy.</summary>
public static partial class EvalChecks
{
    private static readonly string[] Launchers = [".exe", ".cmd", ".bat", ".com"];

    /// <summary>Decides each of <paramref name="checks"/> for one run.</summary>
    /// <param name="checks">The case's checks.</param>
    /// <param name="session">What the session did.</param>
    /// <param name="copyRoot">The copy of the case's repo the session worked in. Its <c>.git</c> is left out of file checks.</param>
    /// <param name="runs">How each <see cref="CheckKind.Run"/> check's command ended, by the command.</param>
    /// <returns>One result per check, in order.</returns>
    /// <exception cref="ArgumentException">A run check's command has no outcome in <paramref name="runs"/>.</exception>
    public static IReadOnlyList<CheckResult> Evaluate(
        IReadOnlyList<EvalCheck> checks,
        SessionActivity session,
        string copyRoot,
        IReadOnlyDictionary<string, RunOutcome> runs)
    {
        var files = new Lazy<List<string>>(() => Files(copyRoot));
        return [.. checks.Select(check => Evaluate(check, session, copyRoot, files, runs))];
    }

    /// <summary>
    /// Whether <paramref name="command"/> ran <paramref name="prefix"/>: whether any of its steps, split at
    /// <c>;</c>, <c>&amp;&amp;</c>, <c>||</c>, <c>|</c> and line breaks, starts with the prefix's words. The first
    /// word is compared by its program's name, without a folder, quotes or a <c>.exe</c>, <c>.cmd</c>, <c>.bat</c>
    /// or <c>.com</c> extension, and a step's leading <c>&amp;</c> (PowerShell's call operator) is skipped. Words
    /// are compared in any case.
    /// </summary>
    /// <param name="command">A command line the session ran.</param>
    /// <param name="prefix">The leading words to look for, such as <c>axm validate</c>.</param>
    /// <returns>Whether a step starts with those words. Always <see langword="false"/> for a blank prefix.</returns>
    public static bool Ran(string command, string prefix)
    {
        var wanted = Words(prefix);
        if (wanted.Count == 0)
        {
            return false;
        }

        wanted[0] = Program(wanted[0]);
        foreach (var step in Steps().Split(command))
        {
            var words = Words(step);
            if (words.Count > 0 && words[0] == "&")
            {
                words.RemoveAt(0);
            }

            if (words.Count < wanted.Count)
            {
                continue;
            }

            words[0] = Program(words[0]);
            if (wanted.Select((word, i) => string.Equals(word, words[i], StringComparison.OrdinalIgnoreCase)).All(same => same))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What <paramref name="check"/> expects, as a short phrase such as <c>axm validate exits 0</c>.</summary>
    /// <param name="check">The check.</param>
    /// <returns>The phrase, the same in every output format.</returns>
    public static string Describe(EvalCheck check) => (check.Kind, check.Not) switch
    {
        (CheckKind.File, false) => check.Contains is null ? $"{check.Target} exists" : $"{check.Target} contains \"{check.Contains}\"",
        (CheckKind.File, true) => check.Contains is null ? $"no file matches {check.Target}" : $"no file matching {check.Target} contains \"{check.Contains}\"",
        (CheckKind.Run, _) => $"{check.Target} exits {check.Exit}",
        (CheckKind.Loaded, false) => $"loaded {check.Target}",
        (CheckKind.Loaded, true) => $"didn't load {check.Target}",
        (CheckKind.Ran, false) => $"ran {check.Target}",
        (CheckKind.Ran, true) => $"didn't run {check.Target}",
        (CheckKind.Reply, false) => $"the reply contains \"{check.Target}\"",
        _ => $"the reply doesn't contain \"{check.Target}\"",
    };

    private static CheckResult Evaluate(
        EvalCheck check,
        SessionActivity session,
        string copyRoot,
        Lazy<List<string>> files,
        IReadOnlyDictionary<string, RunOutcome> runs)
    {
        var (held, observed) = check.Kind switch
        {
            CheckKind.File => File(check, copyRoot, files.Value),
            CheckKind.Run => runs.TryGetValue(check.Target, out var outcome)
                ? (outcome.ExitCode == check.Exit, $"exited {outcome.ExitCode}")
                : throw new ArgumentException($"The run check's command {check.Target} has no outcome.", nameof(runs)),
            CheckKind.Loaded => session.Loads.Contains(check.Target, StringComparer.Ordinal)
                ? (true, "loaded")
                : (false, $"not loaded; the session loaded {Listed(session.Loads)}"),
            CheckKind.Ran => session.Commands.FirstOrDefault(command => Ran(command, check.Target)) is { } command
                ? (true, $"ran {command}")
                : (false, "not run"),
            _ => session.Reply is null ? (false, "no reply")
                : session.Reply.Contains(check.Target, StringComparison.OrdinalIgnoreCase) ? (true, "the reply contains it")
                : (false, "the reply doesn't contain it"),
        };
        return new CheckResult(check, held != check.Not, observed);
    }

    private static (bool Held, string Observed) File(EvalCheck check, string copyRoot, List<string> files)
    {
        // The case's glob was checked when the case was read.
        Glob.TryParse(check.Target, out var glob, out _);
        var matches = files.Where(glob!.IsMatch).ToList();
        if (matches.Count == 0)
        {
            return (false, "no file matches");
        }

        if (check.Contains is null)
        {
            return (true, $"{matches[0]} matches");
        }

        var containing = matches.FirstOrDefault(path =>
            System.IO.File.ReadAllText(Path.Combine(copyRoot, path)).Contains(check.Contains, StringComparison.OrdinalIgnoreCase));
        return containing is null ? (false, "no matching file contains it") : (true, $"{containing} contains it");
    }

    // The copy's files relative to its root, with forward slashes and in ordinal order, so the first match is
    // the same on every platform. The .git folder belongs to the harness, not to the agent's work.
    private static List<string> Files(string copyRoot) =>
    [
        .. Directory.EnumerateFiles(copyRoot, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(copyRoot, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => !path.StartsWith(".git/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal),
    ];

    private static string Listed(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "nothing",
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };

    // Splits a step into words at whitespace outside quotes, and drops the quotes.
    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var inWord = false;
        char? quote = null;
        foreach (var character in text)
        {
            if (quote is not null)
            {
                if (character == quote)
                {
                    quote = null;
                }
                else
                {
                    word.Append(character);
                }
            }
            else if (character is '"' or '\'')
            {
                quote = character;
                inWord = true;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (inWord)
                {
                    words.Add(word.ToString());
                    word.Clear();
                    inWord = false;
                }
            }
            else
            {
                word.Append(character);
                inWord = true;
            }
        }

        if (inWord)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    private static string Program(string word)
    {
        var name = word[(word.LastIndexOfAny(['/', '\\']) + 1)..];
        var launcher = Launchers.FirstOrDefault(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        return launcher is null ? name : name[..^launcher.Length];
    }

    [GeneratedRegex(@"&&|\|\||;|\||\r?\n")]
    private static partial Regex Steps();
}

namespace Axiomarium.Core.Evidence;

/// <summary>How far a check's latest run still vouches for the code as it is now.</summary>
public enum EvidenceState
{
    /// <summary>The latest run passed, and every file it covered is as it was then.</summary>
    Fresh,

    /// <summary>
    /// The latest run passed, but a covered file changed, appeared or went since, the check's covers changed, or its run
    /// entries no longer count the command.
    /// </summary>
    Stale,

    /// <summary>The latest run failed.</summary>
    Failed,

    /// <summary>The check is declared but never ran, or its record can't be read.</summary>
    Missing,
}

/// <summary>A check's status, with what changed since its latest run.</summary>
/// <param name="Check">The check.</param>
/// <param name="State">How far its latest run still vouches for the code.</param>
/// <param name="Record">Its latest run, or <see langword="null"/> when it's missing.</param>
/// <param name="Changed">Covered files whose content changed since, in path order.</param>
/// <param name="Added">Covered files that appeared since, in path order.</param>
/// <param name="Deleted">Files the run covered that are gone, or no longer covered or seen by git, in path order.</param>
/// <param name="CoversChanged">Whether the check's covers changed since it ran.</param>
/// <param name="Reason">Why it's in its state, as a phrase, or <see langword="null"/> when it's fresh.</param>
public sealed record EvidenceStatus(
    EvidenceCheck Check,
    EvidenceState State,
    EvidenceRecord? Record,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Deleted,
    bool CoversChanged,
    string? Reason);

/// <summary>Decides a check's status from its record and what its files hold now.</summary>
public static class EvidenceStatuses
{
    private const int Named = 3;

    /// <summary>The status of <paramref name="check"/> in the repo at <paramref name="repoRoot"/>: its record there, against what the files it covers hold now.</summary>
    /// <param name="repoRoot">The repo's root folder.</param>
    /// <param name="check">The check.</param>
    /// <param name="files">The files git sees, relative to the repo root with forward slashes.</param>
    /// <returns>The status, as <see cref="Of"/> decides it.</returns>
    /// <exception cref="IOException">A covered file can't be read. The message names it.</exception>
    public static EvidenceStatus InRepo(string repoRoot, EvidenceCheck check, IReadOnlyList<string> files)
    {
        var current = EvidenceFiles.Hash(repoRoot, EvidenceFiles.Covered(check, files));
        var (record, problem) = EvidenceRecords.Read(repoRoot, check.Name);
        return Of(check, record, current, problem, path => File.Exists(Path.Combine(repoRoot, path)));
    }

    /// <summary>The status of <paramref name="check"/>, from its latest run and the hashes of the files it covers now.</summary>
    /// <param name="check">The check.</param>
    /// <param name="record">Its latest run, or <see langword="null"/> when it never ran or its record can't be read.</param>
    /// <param name="current">The hash of each file it covers now, by path, as <see cref="EvidenceFiles.Hash"/> gives them.</param>
    /// <param name="problem">Why its record can't be read, which becomes the reason it's missing.</param>
    /// <param name="exists">
    /// Whether a file the run covered still exists, by its path relative to the repo root, so the reason can tell a
    /// deleted file from one that's no longer covered. Left out, every such file reads as deleted.
    /// </param>
    /// <returns>
    /// The status. Freshness comes from content, not time, so undoing an edit makes a check fresh again. A passed
    /// run also goes stale when the check's covers changed, or its run entries no longer count the command. A stale
    /// check's reason names up to three files, with what happened to each, and counts the rest.
    /// </returns>
    public static EvidenceStatus Of(
        EvidenceCheck check, EvidenceRecord? record, IReadOnlyDictionary<string, string> current, string? problem = null, Func<string, bool>? exists = null)
    {
        if (record is null)
        {
            return new EvidenceStatus(check, EvidenceState.Missing, null, [], [], [], false, problem ?? "never run");
        }

        var changed = record.Files.Where(file => current.TryGetValue(file.Key, out var hash) && hash != file.Value).Select(file => file.Key).Order(StringComparer.Ordinal).ToList();
        var added = current.Keys.Where(path => !record.Files.ContainsKey(path)).Order(StringComparer.Ordinal).ToList();
        var deleted = record.Files.Keys.Where(path => !current.ContainsKey(path)).Order(StringComparer.Ordinal).ToList();
        var coversChanged = !record.Covers.SequenceEqual(check.Covers.Select(glob => glob.Pattern), StringComparer.Ordinal);
        if (!record.Passed)
        {
            return new EvidenceStatus(check, EvidenceState.Failed, record, changed, added, deleted, coversChanged, record.Exit is { } exit ? $"exit {exit}" : "it failed");
        }

        var runChanged = !EvidenceCommands.Counts(check, record.Command);
        if (changed.Count + added.Count + deleted.Count == 0 && !coversChanged && !runChanged)
        {
            return new EvidenceStatus(check, EvidenceState.Fresh, record, [], [], [], false, null);
        }

        var parts = new List<string>();
        if (runChanged)
        {
            parts.Add("its run changed");
        }

        if (coversChanged)
        {
            parts.Add("its covers changed");
        }

        // A file can join or leave the covers without being created or deleted, so it's only called new or deleted
        // when nothing else explains it.
        var files = changed.Select(path => $"{path} changed")
            .Concat(added.Select(path => coversChanged ? $"{path} is now covered" : $"{path} is new"))
            .Concat(deleted.Select(path => exists?.Invoke(path) == true ? $"{path} is no longer covered" : $"{path} was deleted"))
            .ToList();
        parts.AddRange(files.Take(Named));
        var more = files.Count - Named;
        if (more > 0)
        {
            parts.Add($"and {more} more {(more == 1 ? "file" : "files")}");
        }

        return new EvidenceStatus(check, EvidenceState.Stale, record, changed, added, deleted, coversChanged, string.Join(", ", parts));
    }
}

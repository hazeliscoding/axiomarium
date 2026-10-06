using Axiomarium.Core.Evidence;

namespace Axiomarium.Cli;

/// <summary>Writes evidence records, the only files <c>axm evidence record</c> and the evidence-freshness hook write.</summary>
internal static class EvidenceStore
{
    /// <summary>Saves <paramref name="record"/> as its check's record in the repo at <paramref name="repoRoot"/>, replacing the last one.</summary>
    /// <param name="repoRoot">The repo's root folder.</param>
    /// <param name="record">The run to record.</param>
    /// <returns>The record's path, relative to the repo root with forward slashes.</returns>
    public static string Save(string repoRoot, EvidenceRecord record)
    {
        var path = EvidenceRecords.PathOf(repoRoot, record.Check);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Written beside it, then moved over it, so a reader, such as the hook in a second session, never sees half a record.
        var written = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(written, EvidenceRecords.ToJson(record));
        File.Move(written, path, overwrite: true);
        return Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
    }
}

using System.Security.Cryptography;

namespace Axiomarium.Core.Evidence;

/// <summary>The files a check vouches for, and what they hold.</summary>
public static class EvidenceFiles
{
    // Records are written here, so covering it would make every check stale the moment one was recorded. A nested
    // .axm/, such as an eval case's scope, is the repo's own content and stays covered.
    private const string AxmFolder = ".axm/";

    /// <summary>The files <paramref name="check"/> covers among <paramref name="files"/>, in their order.</summary>
    /// <param name="check">The check.</param>
    /// <param name="files">The files git sees, relative to the repo root with forward slashes.</param>
    /// <returns>Those its <c>covers</c> globs match, or all of them when it has none, never the repo's own <c>.axm/</c>.</returns>
    public static IReadOnlyList<string> Covered(EvidenceCheck check, IEnumerable<string> files) =>
        [.. files.Where(file => !file.StartsWith(AxmFolder, StringComparison.Ordinal) && (check.Covers.Count == 0 || check.Covers.Any(glob => glob.IsMatch(file))))];

    /// <summary>The <c>covers</c> globs of <paramref name="check"/> that match none of <paramref name="files"/>.</summary>
    /// <param name="check">The check.</param>
    /// <param name="files">The files git sees, relative to the repo root with forward slashes.</param>
    /// <returns>Each glob's pattern, in order. A check that covers nothing would stay fresh whatever changed.</returns>
    public static IReadOnlyList<string> Unmatched(EvidenceCheck check, IEnumerable<string> files)
    {
        var covered = Covered(check, files);
        return [.. check.Covers.Where(glob => !covered.Any(glob.IsMatch)).Select(glob => glob.Pattern)];
    }

    /// <summary>The SHA-256 of each of <paramref name="files"/> under <paramref name="repoRoot"/>.</summary>
    /// <param name="repoRoot">The repo's root folder.</param>
    /// <param name="files">Files relative to it with forward slashes.</param>
    /// <returns>
    /// <c>sha256:</c> and the lowercase hex hash of each file's bytes, by path. A file that no longer exists, as git
    /// still lists a deleted file it tracks, is left out.
    /// </returns>
    /// <exception cref="IOException">A file can't be read, such as one another process holds exclusively, or one the user may not read. The message names the file.</exception>
    public static IReadOnlyDictionary<string, string> Hash(string repoRoot, IEnumerable<string> files)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var path = Path.Combine(repoRoot, file);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                // A file mid-write, such as a log a server appends to, hashes to what it holds now: the next check sees
                // whatever changed after.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                hashes[file] = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream));
            }
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"{file} can't be read: {problem.Message}", problem);
            }
        }

        return hashes;
    }
}

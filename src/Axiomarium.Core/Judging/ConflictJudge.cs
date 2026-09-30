using System.Text;
using System.Text.Json;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Core.Judging;

/// <summary>An instruction file as the judge sees it.</summary>
/// <param name="Shown">Its path as <c>axm</c> shows it: from the repo root, or from <c>~</c>.</param>
/// <param name="Text">What the harness loads of it: all of it, or its first part when the harness cuts it.</param>
public sealed record JudgedFile(string Shown, string Text);

/// <summary>A passage the judge quoted, found in its file.</summary>
/// <param name="File">The file, as <see cref="JudgedFile.Shown"/> names it.</param>
/// <param name="Line">The line the passage starts on, from 1.</param>
/// <param name="Text">The passage, as the judge quoted it.</param>
public sealed record Quote(string File, int Line, string Text);

/// <summary>Two instructions a model judged to contradict each other. It's model judgment, and output says so.</summary>
/// <param name="First">One passage.</param>
/// <param name="Second">The passage it contradicts.</param>
/// <param name="Why">The model's reason, in its words.</param>
public sealed record Contradiction(Quote First, Quote Second, string Why);

/// <summary>What the judge's answer came to.</summary>
/// <param name="Found">The contradictions whose quotes were both found in their files.</param>
/// <param name="Dropped">How many it named whose quotes weren't in the files, or named a file it wasn't shown.</param>
/// <param name="Problem">Why the answer couldn't be read at all, or <see langword="null"/>.</param>
public sealed record ConflictAnswer(IReadOnlyList<Contradiction> Found, int Dropped, string? Problem);

/// <summary>
/// Asks a model for contradictions between the instructions a file gets, and keeps only those it quotes exactly:
/// a made-up contradiction can't cite text that isn't there. The call itself runs through a harness, in the CLI.
/// </summary>
public static class ConflictJudge
{
    /// <summary>The instruction files a harness loads for a file, with what it loads of each.</summary>
    /// <param name="resolution">What the harness loads, from <see cref="Explainer"/>.</param>
    /// <param name="repoRoot">The repo root, which shown paths start from.</param>
    /// <param name="home">The home folder, shown as <c>~</c>.</param>
    /// <returns>Each loaded file once, in context order. A cut file holds only the bytes the harness keeps.</returns>
    public static IReadOnlyList<JudgedFile> Files(Resolution resolution, string repoRoot, string home) =>
    [
        .. resolution.Loaded
            .DistinctBy(item => item.Path, StringComparer.Ordinal)
            .Where(item => File.Exists(item.Path))
            .Select(item =>
            {
                var bytes = File.ReadAllBytes(item.Path);
                var text = Encoding.UTF8.GetString(bytes, 0, item.Cut ? (int)Math.Min(item.Bytes, bytes.Length) : bytes.Length);
                return new JudgedFile(DisplayPath.Of(item.Path, repoRoot, home), text.Replace("\r\n", "\n", StringComparison.Ordinal));
            }),
    ];

    /// <summary>Writes what the judge is asked: each file between markers, and the JSON to answer with.</summary>
    /// <param name="files">The files, in the order the harness loads them.</param>
    /// <param name="previousProblem">Why the last answer couldn't be read, for a retry, or <see langword="null"/>.</param>
    /// <returns>The brief, ending in a newline.</returns>
    public static string Brief(IReadOnlyList<JudgedFile> files, string? previousProblem = null)
    {
        var brief = new StringBuilder()
            .Append("You're reviewing the instructions a coding agent gets while it works on one file, to find instructions that contradict each other: ")
            .Append("two that can't both be followed. Different wording, emphasis or scope isn't a contradiction, and neither is a general rule and a more specific one that narrows it.\n\n")
            .Append("Each file follows between markers, in the order the agent reads them.\n\n");
        foreach (var file in files)
        {
            brief.Append($"=== FILE: {file.Shown} ===\n").Append(file.Text);
            if (!file.Text.EndsWith('\n'))
            {
                brief.Append('\n');
            }

            brief.Append("=== END ===\n\n");
        }

        brief
            .Append("Quote each passage exactly as it's written in its file, a sentence or a line each, and name its file as the marker does. ")
            .Append("Answer with only this JSON, and nothing before or after it, or {\"contradictions\": []} when there are none:\n\n")
            .Append("""{"contradictions": [{"first": {"file": "...", "quote": "..."}, "second": {"file": "...", "quote": "..."}, "why": "..."}]}""")
            .Append('\n');
        if (previousProblem is not null)
        {
            brief.Append($"\nYour previous answer couldn't be used: {previousProblem}. Answer again with only the JSON.\n");
        }

        return brief.ToString();
    }

    /// <summary>Reads the judge's answer and checks every quote against its file.</summary>
    /// <param name="answer">The answer: a JSON object with <c>contradictions</c>, possibly after a line of text or in a code fence.</param>
    /// <param name="files">The files the judge was shown.</param>
    /// <returns>
    /// The contradictions whose two quotes are both in their named files, matched with runs of whitespace read as
    /// one space, and how many others there were. An answer without the JSON has a problem instead.
    /// </returns>
    public static ConflictAnswer Read(string answer, IReadOnlyList<JudgedFile> files)
    {
        var start = answer.IndexOf('{', StringComparison.Ordinal);
        var end = answer.LastIndexOf('}');
        JsonDocument? document = null;
        try
        {
            document = start >= 0 && end > start ? JsonDocument.Parse(answer[start..(end + 1)]) : null;
        }
        catch (JsonException)
        {
        }

        if (document is null)
        {
            return new ConflictAnswer([], 0, "the answer isn't JSON");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("contradictions", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return new ConflictAnswer([], 0, "the answer has no contradictions list");
            }

            var found = new List<Contradiction>();
            var dropped = 0;
            foreach (var item in items.EnumerateArray())
            {
                var first = Find(item, "first", files);
                var second = Find(item, "second", files);
                if (first is null || second is null || first == second)
                {
                    dropped++;
                    continue;
                }

                found.Add(new Contradiction(first, second, Text(item, "why") ?? ""));
            }

            return new ConflictAnswer(found, dropped, null);
        }
    }

    private static Quote? Find(JsonElement item, string name, IReadOnlyList<JudgedFile> files)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var side) || Text(side, "file") is not { } shown || Text(side, "quote") is not { } quote)
        {
            return null;
        }

        var file = files.FirstOrDefault(candidate => candidate.Shown == shown);
        var at = file is null ? -1 : IndexOf(file.Text, quote);
        return at < 0 ? null : new Quote(shown, file!.Text[..at].Count(character => character == '\n') + 1, quote);
    }

    // Where quote starts in text, with every run of whitespace in both read as one space, or -1.
    private static int IndexOf(string text, string quote)
    {
        var wanted = Collapse(quote).Normalized.Trim();
        if (wanted.Length == 0)
        {
            return -1;
        }

        var (normalized, origins) = Collapse(text);
        var at = normalized.IndexOf(wanted, StringComparison.Ordinal);
        return at < 0 ? -1 : origins[at];
    }

    private static (string Normalized, List<int> Origins) Collapse(string text)
    {
        var normalized = new StringBuilder();
        var origins = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                if (normalized.Length > 0 && normalized[^1] == ' ')
                {
                    continue;
                }

                normalized.Append(' ');
            }
            else
            {
                normalized.Append(text[i]);
            }

            origins.Add(i);
        }

        return (normalized.ToString(), origins);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

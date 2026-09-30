using System.Text;
using System.Text.Json;

namespace Axiomarium.Core.Judging;

/// <summary>A model's verdict on one eval run against its case's rubric. It's model judgment, and never changes what the checks decided.</summary>
/// <param name="Passed">Whether the model judged the run to meet the rubric.</param>
/// <param name="Reason">Why, in the model's words.</param>
public sealed record RubricVerdict(bool Passed, string Reason);

/// <summary>What the judge's answer came to.</summary>
/// <param name="Verdict">The verdict, or <see langword="null"/> when the answer couldn't be read.</param>
/// <param name="Problem">Why the answer couldn't be read, or <see langword="null"/>.</param>
public sealed record RubricAnswer(RubricVerdict? Verdict, string? Problem);

/// <summary>Asks a model whether one eval run met its case's rubric, from what the session did. The call itself runs through a harness, in the CLI.</summary>
public static class RubricJudge
{
    /// <summary>Writes what the judge is asked.</summary>
    /// <param name="rubric">The case's <c>judge.rubric</c>.</param>
    /// <param name="prompt">What the user asked the session.</param>
    /// <param name="reply">The session's final message, or <see langword="null"/>.</param>
    /// <param name="commands">The commands the session ran, in order.</param>
    /// <param name="changes">What the session changed in the copy, as a git diff, or empty.</param>
    /// <param name="previousProblem">Why the last answer couldn't be read, for a retry, or <see langword="null"/>.</param>
    /// <returns>The brief, ending in a newline.</returns>
    public static string Brief(string rubric, string prompt, string? reply, IReadOnlyList<string> commands, string changes, string? previousProblem = null)
    {
        var brief = new StringBuilder()
            .Append("You're grading one run of a coding agent against a rubric. Judge only what the rubric asks, not style or anything else.\n\n")
            .Append($"The user asked:\n\n{prompt.TrimEnd()}\n\n")
            .Append($"The rubric:\n\n{rubric.TrimEnd()}\n\n");
        brief.Append(commands.Count == 0 ? "It ran no commands.\n\n" : "The commands it ran, in order:\n\n" + string.Concat(commands.Select(command => $"- {command}\n")) + "\n");
        brief.Append(changes.Trim().Length == 0 ? "It changed no files.\n\n" : $"The changes it made, as a git diff:\n\n{changes.TrimEnd()}\n\n");
        brief.Append(reply is null ? "It ended without a final message.\n\n" : $"Its final message:\n\n{reply.TrimEnd()}\n\n");
        brief
            .Append("Answer with only this JSON, and nothing before or after it, with a reason of one or two sentences:\n\n")
            .Append("""{"passed": true, "reason": "..."}""")
            .Append('\n');
        if (previousProblem is not null)
        {
            brief.Append($"\nYour previous answer couldn't be used: {previousProblem}. Answer again with only the JSON.\n");
        }

        return brief.ToString();
    }

    /// <summary>Reads the judge's answer.</summary>
    /// <param name="answer">The answer: a JSON object with <c>passed</c> and <c>reason</c>, possibly after a line of text or in a code fence.</param>
    /// <returns>The verdict, or why the answer couldn't be read.</returns>
    public static RubricAnswer Read(string answer)
    {
        var start = answer.IndexOf('{', StringComparison.Ordinal);
        var end = answer.LastIndexOf('}');
        try
        {
            if (start < 0 || end <= start)
            {
                return new RubricAnswer(null, "the answer isn't JSON");
            }

            using var document = JsonDocument.Parse(answer[start..(end + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("passed", out var passed) || passed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return new RubricAnswer(null, "the answer has no passed true or false");
            }

            var reason = root.TryGetProperty("reason", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() ?? "" : "";
            return new RubricAnswer(new RubricVerdict(passed.ValueKind == JsonValueKind.True, reason), null);
        }
        catch (JsonException)
        {
            return new RubricAnswer(null, "the answer isn't JSON");
        }
    }
}

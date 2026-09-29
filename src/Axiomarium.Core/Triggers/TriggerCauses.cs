using Axiomarium.Core.Instructions;

namespace Axiomarium.Core.Triggers;

/// <summary>
/// Finds the likely cause of each collision, false trigger and miss in what the harness listed, never by asking
/// a model: models make up reasons after the fact.
/// </summary>
public static class TriggerCauses
{
    private const int WordsShown = 3;

    /// <summary>Gives each problem the first cause that applies.</summary>
    /// <param name="problems">The problems to explain.</param>
    /// <param name="listings">What each harness listed in the test's copy of the repo.</param>
    /// <returns>
    /// The problems, each with its cause: the expected skill isn't listed, with its rule; its description is cut;
    /// it's listed by name only; the listing is over its budget, with the assumption stated; or else the words the
    /// prompt shares with each skill, the rarest in the listing first. When none applies, it says no cause was found.
    /// </returns>
    public static IReadOnlyList<ProblemRuns> Explain(IReadOnlyList<ProblemRuns> problems, IReadOnlyDictionary<Harness, Resolution> listings) =>
        [.. problems.Select(problem => problem with { Cause = listings.TryGetValue(problem.Harness, out var listing) ? Cause(problem, listing) : "no cause found in the listing" })];

    private static string Cause(ProblemRuns problem, Resolution listing)
    {
        if (problem.Expected is { } expected)
        {
            if (Find(listing, expected) is not { } entry)
            {
                return listing.NotListed.FirstOrDefault(skill => Matches(skill.Name, skill.Path, expected)) is { } unlisted
                    ? $"{expected} isn't listed: {unlisted.Rule.Label}"
                    : $"{expected} isn't in the listing";
            }

            if (entry.Cut)
            {
                return $"{expected}'s description is cut short in the listing";
            }

            if (entry.NameOnly)
            {
                return $"{expected} is listed by name only, as skillOverrides sets it";
            }

            if (listing.Listing is { OverBudget: true } budget)
            {
                return $"the listing is over its budget, assuming {budget.Assumption}, so {expected} may be listed by name only";
            }
        }

        var weights = Weights(listing);
        if (problem.Picked is { } picked && Find(listing, picked) is { Path: null })
        {
            return $"{picked} is a built-in skill, whose text isn't recorded";
        }

        var withPicked = problem.Picked is null ? [] : Shared(problem.Text.Prompt, Find(listing, problem.Picked), weights);
        var withExpected = problem.Expected is null ? [] : Shared(problem.Text.Prompt, Find(listing, problem.Expected), weights);
        return problem.Kind switch
        {
            ProblemKind.Collision when withPicked.Count > 0 =>
                $"the prompt shares {Join(withPicked)} with {problem.Picked}, and {(withExpected.Count > 0 ? Join(withExpected) : "nothing")} with {problem.Expected}",
            ProblemKind.FalseTrigger when withPicked.Count > 0 => $"the prompt shares {Join(withPicked)} with {problem.Picked}",
            ProblemKind.Miss when withExpected.Count == 0 => $"the prompt shares no words with {problem.Expected}",
            _ => "no cause found in the listing",
        };
    }

    // How rare each stem is among the listed skills' text: the same smoothed IDF axm triggers weighs overlap with.
    private static Dictionary<string, double> Weights(Resolution listing)
    {
        var texts = listing.Skills.Where(skill => skill.Path is not null).Select(skill => Stems(Text(skill))).ToList();
        return texts.SelectMany(stems => stems).GroupBy(stem => stem, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Math.Log(1 + (double)texts.Count / group.Count()), StringComparer.Ordinal);
    }

    // The prompt's words that the skill's text shares, heaviest first, then in the order the prompt uses them.
    private static List<string> Shared(string prompt, AvailableSkill? skill, Dictionary<string, double> weights)
    {
        if (skill is null)
        {
            return [];
        }

        var stems = Stems(Text(skill));
        return [.. Terms.Of(prompt)
            .Where(term => stems.Contains(term.Stem))
            .DistinctBy(term => term.Stem, StringComparer.Ordinal)
            .Select((term, order) => (term.Word, Weight: weights.GetValueOrDefault(term.Stem), order))
            .OrderByDescending(term => term.Weight).ThenBy(term => term.order)
            .Take(WordsShown)
            .Select(term => term.Word)];
    }

    private static HashSet<string> Stems(string text) => [.. Terms.Of(text).Select(term => term.Stem)];

    // A skill's words are its name, without a namespace, and the text its entry shows.
    private static string Text(AvailableSkill skill) => $"{skill.Text} {skill.Name[(skill.Name.LastIndexOf(':') + 1)..]}";

    // Codex's loads are named by folder, and its listing by the frontmatter name, so either matches.
    private static AvailableSkill? Find(Resolution listing, string name) => listing.Skills.FirstOrDefault(skill => Matches(skill.Name, skill.Path, name));

    private static bool Matches(string listed, string? path, string name) =>
        listed == name || (path is not null && Path.GetFileName(Path.GetDirectoryName(path)) == name);

    private static string Join(List<string> words) => words.Count == 1 ? words[0] : $"{string.Join(", ", words[..^1])} and {words[^1]}";
}

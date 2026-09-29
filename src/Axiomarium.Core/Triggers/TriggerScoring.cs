using Axiomarium.Core.Instructions;

namespace Axiomarium.Core.Triggers;

/// <summary>What went wrong in a run.</summary>
public enum ProblemKind
{
    /// <summary>A skill other than the one expected fired.</summary>
    Collision,

    /// <summary>The skill under test fired on a prompt that shouldn't pick it, and no other skill was expected.</summary>
    FalseTrigger,

    /// <summary>No skill fired when the skill under test should have.</summary>
    Miss,
}

/// <summary>The runs of one prompt on one harness that went wrong the same way.</summary>
/// <param name="Harness">The harness.</param>
/// <param name="Skill">The skill under test, whose prompt it is.</param>
/// <param name="Prompt">The prompt's index in the skill's prompt file.</param>
/// <param name="Text">The prompt.</param>
/// <param name="Kind">What went wrong.</param>
/// <param name="Expected">The skill that should have fired: the skill under test, or an ambiguous prompt's rival. <see langword="null"/> for a false trigger.</param>
/// <param name="Picked">The skill that fired instead, or <see langword="null"/> for a miss.</param>
/// <param name="Count">How many runs went this way.</param>
/// <param name="Runs">How many runs of the prompt were scored.</param>
/// <param name="Cause">The likely cause, from the listing, or <see langword="null"/> before causes are found.</param>
public sealed record ProblemRuns(
    Harness Harness, string Skill, int Prompt, TriggerPrompt Text, ProblemKind Kind, string? Expected, string? Picked, int Count, int Runs, string? Cause = null);

/// <summary>A skill's precision and recall on one harness, counted in runs over its own prompts.</summary>
/// <param name="Harness">The harness.</param>
/// <param name="Skill">The skill under test.</param>
/// <param name="TruePositives">Runs that should have picked it and did.</param>
/// <param name="FalsePositives">Runs that picked it and shouldn't have.</param>
/// <param name="FalseNegatives">Runs that should have picked it and didn't.</param>
public sealed record SkillScore(Harness Harness, string Skill, int TruePositives, int FalsePositives, int FalseNegatives)
{
    /// <summary>The share of runs that picked the skill and should have, or <see langword="null"/> when none picked it.</summary>
    public double? Precision => TruePositives + FalsePositives == 0 ? null : TruePositives / (double)(TruePositives + FalsePositives);

    /// <summary>The share of runs that should have picked the skill and did, or <see langword="null"/> when none should have.</summary>
    public double? Recall => TruePositives + FalseNegatives == 0 ? null : TruePositives / (double)(TruePositives + FalseNegatives);
}

/// <summary>A skill loaded before every task on a harness, which is therefore never counted as a pick.</summary>
/// <param name="Harness">The harness.</param>
/// <param name="Name">The skill.</param>
/// <param name="Runs">How many scored runs loaded it.</param>
/// <param name="Of">How many runs were scored on the harness.</param>
public sealed record BackgroundSkill(Harness Harness, string Name, int Runs, int Of);

/// <summary>A trigger test's scores.</summary>
/// <param name="Scores">Each skill's precision and recall on each harness, Claude Code first, then by skill.</param>
/// <param name="Problems">Every collision, false trigger and miss, by harness, skill, kind and prompt.</param>
/// <param name="Background">The background skills on each harness.</param>
/// <param name="Failed">The sessions that gave no answer, which aren't scored.</param>
public sealed record TriggerResults(
    IReadOnlyList<SkillScore> Scores, IReadOnlyList<ProblemRuns> Problems, IReadOnlyList<BackgroundSkill> Background, IReadOnlyList<SessionResult> Failed);

/// <summary>Scores a trigger test's sessions: which skill each run picked, and how often that was right.</summary>
public static class TriggerScoring
{
    /// <summary>Scores <paramref name="results"/>.</summary>
    /// <param name="results">Every session's result.</param>
    /// <param name="prompts">The prompt file of each skill under test, by skill name.</param>
    /// <returns>
    /// The scores, with each run's pick being its first load that isn't a background skill. A skill is background
    /// on a harness when it isn't under test and every scored run there loaded it, across at least two prompts,
    /// such as a plugin skill that tells the agent to use skills before anything else.
    /// </returns>
    public static TriggerResults Score(IReadOnlyList<SessionResult> results, IReadOnlyDictionary<string, TriggerPromptFile> prompts)
    {
        var scored = results.Where(result => result.Problem is null).ToList();
        var background = new List<BackgroundSkill>();
        foreach (var harness in scored.GroupBy(result => result.Session.Harness).OrderBy(group => group.Key))
        {
            var runs = harness.ToList();
            if (runs.Select(result => (result.Session.Skill, result.Session.Prompt)).Distinct().Count() < 2)
            {
                continue;
            }

            background.AddRange(runs
                .SelectMany(result => result.Loads.Distinct(StringComparer.Ordinal))
                .GroupBy(name => name, StringComparer.Ordinal)
                .Where(name => name.Count() == runs.Count && !prompts.ContainsKey(name.Key))
                .Select(name => new BackgroundSkill(harness.Key, name.Key, runs.Count, runs.Count))
                .OrderBy(skill => skill.Name, StringComparer.Ordinal));
        }

        string? Pick(SessionResult result) =>
            result.Loads.FirstOrDefault(name => !background.Any(skill => skill.Harness == result.Session.Harness && skill.Name == name));

        var scores = new List<SkillScore>();
        var problems = new List<ProblemRuns>();
        foreach (var group in scored.GroupBy(result => (result.Session.Harness, result.Session.Skill)).OrderBy(group => group.Key.Harness).ThenBy(group => group.Key.Skill, StringComparer.Ordinal))
        {
            var (harness, skill) = group.Key;
            int truePositives = 0, falsePositives = 0, falseNegatives = 0;
            foreach (var prompt in group.GroupBy(result => result.Session.Prompt).OrderBy(prompt => prompt.Key))
            {
                var text = prompts[skill].Prompts[prompt.Key];
                var picks = prompt.Select(Pick).ToList();
                var outcomes = new List<(ProblemKind Kind, string? Expected, string? Picked)>();
                foreach (var pick in picks)
                {
                    if (text.ShouldTrigger && pick == skill)
                    {
                        truePositives++;
                    }
                    else if (text.ShouldTrigger)
                    {
                        falseNegatives++;
                        outcomes.Add(pick is null ? (ProblemKind.Miss, skill, null) : (ProblemKind.Collision, skill, pick));
                    }
                    else if (pick == skill)
                    {
                        falsePositives++;
                        outcomes.Add(text is { Kind: PromptKind.Ambiguous, Rival: { } rival } ? (ProblemKind.Collision, rival, skill) : (ProblemKind.FalseTrigger, null, skill));
                    }
                }

                problems.AddRange(outcomes
                    .GroupBy(outcome => outcome)
                    .Select(outcome => new ProblemRuns(harness, skill, prompt.Key, text, outcome.Key.Kind, outcome.Key.Expected, outcome.Key.Picked, outcome.Count(), picks.Count)));
            }

            scores.Add(new SkillScore(harness, skill, truePositives, falsePositives, falseNegatives));
        }

        return new TriggerResults(
            scores,
            [.. problems.OrderBy(problem => problem.Harness).ThenBy(problem => problem.Skill, StringComparer.Ordinal).ThenBy(problem => problem.Kind).ThenBy(problem => problem.Prompt)],
            background,
            [.. results.Where(result => result.Problem is not null)]);
    }
}

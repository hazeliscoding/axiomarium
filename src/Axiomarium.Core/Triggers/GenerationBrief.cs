using System.Text;

namespace Axiomarium.Core.Triggers;

/// <summary>
/// Writes what <c>axm triggers generate</c> asks the model for: trigger prompts of each kind for one skill,
/// shown as its listing entry next to the entries of the skills it overlaps, and answered as JSON.
/// </summary>
public static class GenerationBrief
{
    // How many prompts of each kind. Ambiguous ones need a rival to sit between.
    private static readonly (PromptKind Kind, int Count, string Ask)[] Mix =
    [
        (PromptKind.Positive, 5, "requests the skill is for. should_trigger: true."),
        (PromptKind.Paraphrased, 4, "requests the skill is for, worded without the words of its description. should_trigger: true."),
        (PromptKind.Negative, 4, "ordinary coding requests the skill has nothing to do with. should_trigger: false."),
        (PromptKind.Adversarial, 4, "requests that use the skill's words but need something else. should_trigger: false."),
        (PromptKind.Ambiguous, 3, "requests between the skill and one of the skills it could be confused with. Set rival to that skill's name, and should_trigger to whether this skill is the better choice."),
    ];

    /// <summary>Writes the brief.</summary>
    /// <param name="skill">The skill's name, as its listing shows it.</param>
    /// <param name="text">What its listing entry shows after the name.</param>
    /// <param name="rivals">The skills it overlaps, with their entries' text. With none, no ambiguous prompts are asked for.</param>
    /// <param name="previousProblem">Why the last answer couldn't be used, for a retry, or <see langword="null"/>.</param>
    /// <returns>The brief, ending in a newline.</returns>
    public static string Build(string skill, string text, IReadOnlyList<(string Name, string Text)> rivals, string? previousProblem = null)
    {
        var mix = Mix.Where(entry => entry.Kind != PromptKind.Ambiguous || rivals.Count > 0).ToList();
        var brief = new StringBuilder()
            .Append("You're writing test prompts for an agent skill, to check whether a coding agent picks the skill when it should and leaves it alone when it shouldn't. ")
            .Append("The agent sees each skill's name and description in a list, and decides which one, if any, to load for the user's request.\n\n")
            .Append("The skill under test:\n\n")
            .Append($"- {skill}: {text}\n");
        if (rivals.Count > 0)
        {
            brief.Append("\nSkills in the same list it could be confused with:\n\n");
            foreach (var (name, rivalText) in rivals)
            {
                brief.Append($"- {name}: {rivalText}\n");
            }
        }

        brief.Append($"\nWrite {mix.Sum(entry => entry.Count)} prompts, each a request a user might type as the first message of a session in a repository:\n\n");
        foreach (var (kind, count, ask) in mix)
        {
            brief.Append($"- {count} {TriggerPrompts.Name(kind)}: {ask}\n");
        }

        brief
            .Append("\nKeep each prompt to one or two sentences in the user's voice, don't name the skill, and make the prompts differ from one another.\n\n")
            .Append("Answer with only this JSON, and nothing before or after it:\n\n")
            .Append("""{"prompts": [{"prompt": "...", "kind": "positive", "should_trigger": true}""")
            .Append(rivals.Count > 0 ? """, {"prompt": "...", "kind": "ambiguous", "should_trigger": false, "rival": "..."}""" : "")
            .Append("]}\n");
        if (previousProblem is not null)
        {
            brief.Append($"\nYour previous answer couldn't be used: {previousProblem}. Answer again with only the JSON.\n");
        }

        return brief.ToString();
    }
}

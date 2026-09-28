namespace Axiomarium.Core.Instructions;

/// <summary>
/// A skill a harness lists for the model. The model decides whether to invoke it, so a listed skill is
/// available, never loaded.
/// </summary>
/// <param name="Name">The name the listing shows, such as <c>deploy</c> or <c>plugin:deploy</c>.</param>
/// <param name="Path">Its <c>SKILL.md</c> or command file, or <see langword="null"/> for a skill built into the harness.</param>
/// <param name="Timing">When it joins the listing: at launch, or when the agent reads or edits a file it applies to.</param>
/// <param name="Rule">The rule that lists it.</param>
/// <param name="Chars">How many characters its entry takes in the listing, which count against the listing's budget.</param>
/// <param name="Cut">Whether its description is cut at the harness's cap for one entry.</param>
/// <param name="NameOnly">Whether a setting lists it by name, without its description.</param>
/// <param name="Patterns">The patterns that matched the file, for a skill that joins by its <c>paths</c>.</param>
public sealed record AvailableSkill(
    string Name, string? Path, LoadTiming Timing, HarnessRule Rule, int Chars, bool Cut = false, bool NameOnly = false, IReadOnlyList<string>? Patterns = null);

/// <summary>A skill the harness knows about but doesn't list for the model, for this file or at all.</summary>
/// <param name="Name">The skill's name.</param>
/// <param name="Path">Its <c>SKILL.md</c> or command file, or <see langword="null"/> for a skill built into the harness.</param>
/// <param name="Rule">The rule that keeps it out of the listing.</param>
public sealed record UnlistedSkill(string Name, string? Path, HarnessRule Rule);

/// <summary>The size of a harness's skill listing at launch, against the budget past which it cuts descriptions.</summary>
/// <param name="Chars">The characters the listing takes, built-in skills included.</param>
/// <param name="Budget">The characters the harness allows before it cuts or drops descriptions.</param>
/// <param name="Assumption">What the budget assumes that <c>axm</c> can't read, such as the model's context window.</param>
/// <param name="Rule">The rule that sets the budget.</param>
public sealed record SkillListing(int Chars, int Budget, string Assumption, HarnessRule Rule)
{
    /// <summary>Whether the listing is over its budget, so the harness cuts or drops some descriptions.</summary>
    public bool OverBudget => Chars > Budget;
}

using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Tests.Core;

public class TriggerCausesTests
{
    private static AvailableSkill Listed(string name, string text, bool cut = false, bool nameOnly = false) =>
        new(name, $"/repo/.claude/skills/{name}/SKILL.md", LoadTiming.AtLaunch, ClaudeCodeSkillRules.ProjectSkill, text.Length, cut, nameOnly, Text: nameOnly ? null : text);

    private static Resolution Listing(AvailableSkill[] skills, UnlistedSkill[]? notListed = null, bool overBudget = false) =>
        new([], [])
        {
            Skills = [.. skills, new AvailableSkill("init", null, LoadTiming.AtLaunch, ClaudeCodeSkillRules.BuiltIn, 67)],
            NotListed = notListed ?? [],
            Listing = new SkillListing(overBudget ? 9000 : 100, 8000, "characters", "a 200k-token context window", ClaudeCodeSkillRules.ListingBudget),
        };

    private static readonly AvailableSkill Deploy = Listed("deploy", "Deploys the shop to production.");
    private static readonly AvailableSkill Ship = Listed("ship", "Cuts a release, bumps the version and tags it.");
    private static readonly AvailableSkill Tables = Listed("tables", "Formats markdown tables.");

    private static string Cause(Resolution listing, ProblemKind kind, string prompt, string? expected, string? picked) =>
        TriggerCauses.Explain(
            [new ProblemRuns(Harness.ClaudeCode, "deploy", 0, new TriggerPrompt(prompt, PromptKind.Positive, true), kind, expected, picked, 1, 3)],
            new Dictionary<Harness, Resolution> { [Harness.ClaudeCode] = listing })
            .Single().Cause!;

    [Fact]
    public void An_expected_skill_that_is_not_listed_says_which_rule_keeps_it_out()
    {
        var listing = Listing([Ship, Tables], [new UnlistedSkill("deploy", "/repo/.claude/skills/deploy/SKILL.md", ClaudeCodeSkillRules.Shadowed)]);

        Assert.Equal("deploy isn't listed: another skill has its name", Cause(listing, ProblemKind.Miss, "Ship the shop.", "deploy", null));
        Assert.Equal("deploy isn't in the listing", Cause(Listing([Ship]), ProblemKind.Miss, "Ship the shop.", "deploy", null));
    }

    [Fact]
    public void A_cut_or_name_only_entry_comes_before_the_wording()
    {
        const string prompt = "Cut a release of the shop.";

        Assert.Equal("deploy's description is cut short in the listing", Cause(Listing([Listed("deploy", "Deploys.", cut: true), Ship]), ProblemKind.Collision, prompt, "deploy", "ship"));
        Assert.Equal("deploy is listed by name only, as skillOverrides sets it", Cause(Listing([Listed("deploy", "Deploys.", nameOnly: true), Ship]), ProblemKind.Collision, prompt, "deploy", "ship"));
    }

    // The first real run gave every Codex problem the same over-budget cause, which hid the wording. The budget
    // is a fact about the whole listing, so it's said once, for the harness.
    [Fact]
    public void A_listing_over_budget_is_a_note_for_the_harness_and_each_problem_keeps_its_wording()
    {
        var listing = Listing([Deploy, Ship, Tables], overBudget: true);
        var codex = new Resolution([], [])
        {
            Skills = [Deploy],
            Listing = new SkillListing(8470, 5440, "tokens", "2% of a 272k-token context window", CodexSkillRules.ListingBudget),
        };

        Assert.Equal("the prompt shares cut and release with ship, and shop with deploy", Cause(listing, ProblemKind.Collision, "Cut a release of the shop.", "deploy", "ship"));
        Assert.Equal(
            "its skill listing is over budget, 9,000 of 8,000 characters assuming a 200k-token context window, so some skills are listed by name only",
            TriggerCauses.BudgetNote(listing));
        Assert.Equal(
            "its skill listing is over budget, about 8,470 of 5,440 tokens assuming 2% of a 272k-token context window, so descriptions are shortened and skills dropped from the end",
            TriggerCauses.BudgetNote(codex));
        Assert.Null(TriggerCauses.BudgetNote(Listing([Deploy])));
    }

    // Terms weigh more the rarer they are in the listing, as axm triggers weighs them.
    [Fact]
    public void A_collision_names_the_words_the_prompt_shares_with_each_skill()
    {
        var listing = Listing([Deploy, Ship, Tables]);

        Assert.Equal(
            "the prompt shares bump, version and cut with ship, and shop with deploy",
            Cause(listing, ProblemKind.Collision, "Bump the version and cut a release of the shop.", "deploy", "ship"));
        Assert.Equal(
            "the prompt shares cut and release with ship, and nothing with deploy",
            Cause(listing, ProblemKind.Collision, "Cut a release.", "deploy", "ship"));
    }

    [Fact]
    public void A_false_trigger_names_what_the_prompt_shares_with_the_skill_and_a_miss_says_when_it_shares_nothing()
    {
        var listing = Listing([Deploy, Ship, Tables]);

        Assert.Equal("the prompt shares production with deploy", Cause(listing, ProblemKind.FalseTrigger, "Check the production logs.", null, "deploy"));
        Assert.Equal("the prompt shares no words with deploy", Cause(listing, ProblemKind.Miss, "Get v2 out the door.", "deploy", null));
    }

    [Fact]
    public void Without_evidence_in_the_listing_it_says_so()
    {
        var listing = Listing([Deploy, Ship, Tables]);

        Assert.Equal("init is a built-in skill, whose text isn't recorded", Cause(listing, ProblemKind.Collision, "Set up the shop.", "deploy", "init"));
        Assert.Equal("no cause found in the listing", Cause(listing, ProblemKind.Collision, "Get v2 out the door.", "deploy", "tables"));
        Assert.Equal("no cause found in the listing", Cause(listing, ProblemKind.Miss, "Deploy the shop.", "deploy", null));
    }
}
